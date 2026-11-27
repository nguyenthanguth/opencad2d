"""Runs one function over many items in several processes, with the same answers however many processes there are.

A pipeline that makes or scores thousands of drawings (episodes of a placement agent, a dataset) runs faster on every core,
but a pool that hands each item a random state gives different numbers on every run. ``run_many`` gives each item a seed that
is a function of the run's seed and of the item's place in the list only, so the answer of an item does not depend on the
number of workers, on which worker took it, or on the order in which the items finished.

Each worker is a fresh process (``spawn``, as Windows has no other) that imports ``opencad2d.cad`` once, when it starts: the
.NET runtime and the assemblies are loaded once per worker, not once per item. What a process prepares for itself stays with
it: the fonts the renderer scans, the Skia library it loads, a display list a drawing built. A render draws one picture at a
time in a process (the renderer's caches are not locked), which is why the work is spread over processes and not threads.
A script that calls ``run_many`` must guard its start with ``if __name__ == "__main__":``, as any ``spawn`` pool needs, and
the function and the items must be picklable: a function defined at the top of a module, not a lambda.
"""

from __future__ import annotations

import hashlib
import importlib
import multiprocessing
import os
import time
import traceback
from collections.abc import Callable, Iterable
from concurrent.futures import Future, ProcessPoolExecutor
from dataclasses import dataclass
from multiprocessing.connection import Connection, PipeConnection, wait
from multiprocessing.process import BaseProcess
from typing import Any, Generic, TypeAlias, TypeVar

T = TypeVar("T")
R = TypeVar("R")

MAX_WORKERS: int = 61
"""The most worker processes a run uses: Windows cannot wait on more than 61 at once, and ``ProcessPoolExecutor`` fails above it."""

_Link: TypeAlias = "Connection[Any, Any] | PipeConnection[Any, Any]"
"""A pipe to a worker: Windows gives a ``PipeConnection``, which this app is for."""


@dataclass(frozen=True)
class Outcome(Generic[R]):
    """What one item came to: ``index`` its place in the list, ``seed`` the seed it was given, ``value`` what the function
    returned (None when it raised), ``error`` the formatted traceback of what it raised (None when it returned; the last line
    is the exception's name and message) and ``seconds`` the time the function took in its process."""

    index: int
    seed: int
    value: R | None
    error: str | None
    seconds: float

    @property
    def ok(self) -> bool:
        """Say whether the function returned: True when ``error`` is None.

        Example:
            [o.ok for o in run_many(work, [1, 2], workers=1)]    # [True, True]
        """
        return self.error is None


def item_seed(seed: int, index: int) -> int:
    """Give the seed of the item at ``index`` of a run of ``seed``: a number of 63 bits, the same on every machine, Python and
    run, that is a function of the two numbers alone (a hash, not their sum, so that the seeds of two runs do not shift into
    each other).

    Example:
        item_seed(7, 0) == item_seed(7, 0)       # True
        item_seed(7, 0) != item_seed(7, 1)       # True
        item_seed(7, 0) != item_seed(8, 0)       # True
    """
    digest = hashlib.sha256(f"opencad2d:{seed}:{index}".encode("ascii")).digest()
    return int.from_bytes(digest[:8], "big") >> 1


def _start() -> None:
    """Starts a worker: loads .NET and the assemblies once, for every item it takes."""
    importlib.import_module("opencad2d.cad")


def _call(function: Callable[[T, int], R], index: int, item: T, seed: int) -> Outcome[R]:
    """Runs one item, in a worker or in the caller's process, and gives what it came to: an exception is an ``error``."""
    started = time.perf_counter()
    try:
        value = function(item, seed)
    except Exception:  # noqa: BLE001  (every failure of an item is that item's result)
        return Outcome(index, seed, None, traceback.format_exc().rstrip(), time.perf_counter() - started)
    return Outcome(index, seed, value, None, time.perf_counter() - started)


@dataclass
class _Slot:
    """One worker of a timed run: its process, the pipe to it, whether it has loaded .NET and, while it has an item, which one and when it began."""

    process: BaseProcess
    connection: _Link
    ready: bool = False
    index: int | None = None
    started: float = 0.0


def _serve(connection: _Link, function: Callable[[Any, int], Any]) -> None:
    """The loop of a worker of a timed run: loads .NET, says it is ready, then answers each item it is sent until it is sent None."""
    _start()
    connection.send(None)
    while True:
        job = connection.recv()
        if job is None:
            return
        index, item, seed = job
        outcome = _call(function, index, item, seed)
        try:
            connection.send(outcome)
        except Exception as error:  # noqa: BLE001  (a value that does not pickle is that item's error)
            connection.send(Outcome(index, seed, None, f"{type(error).__name__}: {error}", outcome.seconds))


def _run_timed(
    function: Callable[[T, int], R], listed: list[T], seeds: list[int], count: int, timeout: float
) -> list[Outcome[R]]:
    """Runs the items in ``count`` worker processes of its own, each item for no longer than ``timeout`` seconds: the process of an
    item that runs over is stopped and a new one is started in its place, which is why this is not a ``ProcessPoolExecutor``."""
    context = multiprocessing.get_context("spawn")
    outcomes: list[Outcome[R] | None] = [None] * len(listed)
    slots: list[_Slot] = []

    def hire() -> _Slot:
        mine, theirs = context.Pipe()
        process = context.Process(target=_serve, args=(theirs, function), daemon=True)
        process.start()
        theirs.close()
        return _Slot(process, mine)

    def fire(slot: _Slot) -> None:
        slot.process.terminate()
        slot.process.join()
        slot.connection.close()

    def replace(slot: _Slot) -> None:
        fire(slot)
        slots[slots.index(slot)] = hire()

    try:
        slots.extend(hire() for _ in range(min(count, len(listed))))
        following = 0
        while any(outcome is None for outcome in outcomes):
            for slot in slots:
                if slot.ready and slot.index is None and following < len(listed):
                    try:
                        slot.connection.send((following, listed[following], seeds[following]))
                        slot.index, slot.started = following, time.monotonic()
                    except Exception as error:  # noqa: BLE001  (an item that does not pickle is its own error)
                        outcomes[following] = Outcome(following, seeds[following], None, f"{type(error).__name__}: {error}", 0.0)
                    following += 1
            now = time.monotonic()
            running = [timeout - (now - slot.started) for slot in slots if slot.index is not None]
            for connection in wait([slot.connection for slot in slots], max(0.0, min(running)) if running else None):
                slot = next(s for s in slots if s.connection is connection)
                try:
                    message = slot.connection.recv()
                except (EOFError, OSError):
                    if slot.index is None:
                        raise RuntimeError("A worker process ended before it had started.") from None
                    outcomes[slot.index] = Outcome(
                        slot.index, seeds[slot.index], None, "BrokenProcessPool: the worker process ended", 0.0
                    )
                    replace(slot)
                    continue
                if slot.index is None:
                    slot.ready = True
                else:
                    outcomes[slot.index] = message
                    slot.index = None
            now = time.monotonic()
            for slot in list(slots):
                if slot.index is not None and now - slot.started > timeout:
                    outcomes[slot.index] = Outcome(
                        slot.index,
                        seeds[slot.index],
                        None,
                        f"TimeoutError: the item took longer than {timeout} s and its worker was replaced",
                        now - slot.started,
                    )
                    replace(slot)
        return [outcome for outcome in outcomes if outcome is not None]
    finally:
        for slot in slots:
            fire(slot)


def run_many(
    function: Callable[[T, int], R],
    items: Iterable[T],
    workers: int | None = None,
    seed: int = 0,
    timeout: float | None = None,
) -> list[Outcome[R]]:
    """Run ``function(item, item_seed)`` for each item and give the ``Outcome`` of each, in the order of ``items``.

    The seed of an item is ``item_seed(seed, index)``; a function that draws its random numbers from it (``random.Random(s)``,
    ``synth.generate(s)``) gives the same value for any ``workers`` and on every run. ``workers`` is the number of processes
    (None: one per core; never more than there are items or ``MAX_WORKERS``, 61, which Windows allows a pool, so a larger
    number is 61); 1 runs the items one after the other in the caller's process,
    which starts no pool and loads nothing again. An item that raises is an ``Outcome`` with ``error`` and the others go on;
    a worker that dies (the process killed, memory out) fails the items still waiting or running, each with an error, as the
    pool is gone with it, and keeps the values already collected. Ctrl+C (or any exception out of the call) cancels the items
    not yet started and raises at once; those running finish in the background. The function, the items and the values must be picklable for ``workers`` above 1.

    ``timeout`` is the seconds one item may take (None: no limit). An item that runs over it is an ``Outcome`` with an ``error`` that
    starts ``TimeoutError`` and ``seconds`` as long as it ran, its worker process is stopped and a new one takes its place, and the
    other items go on, the results still in the order of ``items``. The time counts from the moment a worker that has loaded .NET
    starts the item, not from the start of the run. A timeout is a limit on a process, so a run with a ``timeout`` uses worker
    processes even for ``workers=1``. Raises ``ValueError`` for ``workers`` below 1 or a ``timeout`` that is not above 0.

    Example:
        def work(item: int, seed: int) -> float:                      # at the top of a module
            return random.Random(seed).random() * item

        if __name__ == "__main__":
            outcomes = run_many(work, [1, 2, 3, 4], workers=3, seed=42)
            [o.ok for o in outcomes]                                  # [True, True, True, True]
            [o.value for o in outcomes] == [o.value for o in run_many(work, [1, 2, 3, 4], workers=1, seed=42)]   # True
    """
    listed = list(items)
    if workers is not None and workers < 1:
        raise ValueError(f"workers is 1 or more, not {workers}.")
    if timeout is not None and not timeout > 0.0:
        raise ValueError(f"timeout is above 0 seconds, not {timeout}.")
    count = min(workers or os.cpu_count() or 1, len(listed), MAX_WORKERS)
    seeds = [item_seed(seed, index) for index in range(len(listed))]
    if timeout is not None and listed:
        return _run_timed(function, listed, seeds, max(count, 1), timeout)
    if count <= 1:
        return [_call(function, index, item, seeds[index]) for index, item in enumerate(listed)]
    outcomes: list[Outcome[R]] = []
    pool = ProcessPoolExecutor(max_workers=count, mp_context=multiprocessing.get_context("spawn"), initializer=_start)
    try:
        pending: list[Future[Outcome[R]] | Exception] = []
        for index, item in enumerate(listed):
            try:
                pending.append(pool.submit(_call, function, index, item, seeds[index]))
            except Exception as error:  # noqa: BLE001  (the pool broke while items were being handed to it)
                pending.append(error)
        for index, future in enumerate(pending):
            try:
                if isinstance(future, Exception):
                    raise future
                outcomes.append(future.result())
            except Exception as error:  # noqa: BLE001  (a worker that died, a value that does not pickle)
                outcomes.append(Outcome(index, seeds[index], None, f"{type(error).__name__}: {error}", 0.0))
    except BaseException:
        pool.shutdown(wait=False, cancel_futures=True)
        raise
    pool.shutdown()
    return outcomes
