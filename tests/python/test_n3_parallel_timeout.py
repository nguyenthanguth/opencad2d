"""N3: the timeout of ``opencad2d.parallel.run_many``: an item that runs over it is a timed-out error, its worker is replaced, the
other items go on and the results stay in the order of the items."""

import math
import multiprocessing
import threading
import time
from pathlib import Path

import pytest

import opencad2d  # noqa: F401  (boots .NET and loads the assemblies)
from opencad2d import parallel
from opencad2d.parallel import run_many

import parallel_start_dies as start_dies
import parallel_start_hangs as start_hangs
import parallel_timeout_work as work


@pytest.mark.parametrize("workers", [1, 2])
@pytest.mark.timing
def test_an_item_over_the_timeout_is_an_error_and_the_others_still_come_in_order(workers: int) -> None:
    started = time.perf_counter()
    outcomes = run_many(work.hangs_on_two, list(range(6)), workers=workers, timeout=3.0)
    took = time.perf_counter() - started

    assert [o.index for o in outcomes] == list(range(6))
    assert [o.ok for o in outcomes] == [True, True, False, True, True, True]
    assert [o.value[0] for o in outcomes if o.value is not None] == [0, 1, 3, 4, 5]
    late = outcomes[2]
    assert late.value is None
    assert late.error is not None and late.error.startswith("TimeoutError")
    assert 3.0 <= late.seconds < 10.0
    assert took < 40.0  # nothing waited out the minute the item slept


def test_a_timeout_that_is_not_above_nought_is_refused() -> None:
    with pytest.raises(ValueError, match="timeout is above 0"):
        run_many(work.hangs_on_two, [1], workers=1, timeout=0.0)


def test_with_no_timeout_the_run_is_as_it_was() -> None:
    outcomes = run_many(work.hangs_on_two, [0, 1], workers=1)

    assert [o.ok for o in outcomes] == [True, True]


@pytest.mark.parametrize("workers", [1, 2])
def test_the_worker_of_the_item_that_ran_over_is_stopped_and_no_later_item_runs_in_it(tmp_path: Path, workers: int) -> None:
    noted = tmp_path / "hung.txt"
    outcomes = run_many(work.noted, [(index, str(noted)) for index in range(6)], workers=workers, timeout=3.0)

    hung = int(noted.read_text(encoding="ascii"))
    later = [o.value[1] for o in outcomes[3:] if o.value is not None]
    assert len(later) == 3
    assert hung not in later
    assert hung not in {child.pid for child in multiprocessing.active_children()}


def test_a_timeout_that_is_not_finite_is_refused() -> None:
    for refused in (math.inf, math.nan, -1.0):
        with pytest.raises(ValueError, match="timeout is above 0"):
            run_many(work.hangs_on_two, [1], workers=1, timeout=refused)


@pytest.mark.timing
def test_workers_that_end_while_they_start_give_every_item_an_error_and_the_call_returns() -> None:
    started = time.perf_counter()
    outcomes = run_many(start_dies.work, list(range(5)), workers=2, timeout=5.0)

    assert [o.index for o in outcomes] == list(range(5))
    assert all(not o.ok and o.error is not None and o.error.startswith("WorkerStartError") for o in outcomes)
    assert time.perf_counter() - started < 60.0


@pytest.mark.timing
def test_a_worker_that_says_nothing_is_given_up_after_the_start_up_deadline(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(parallel, "START_TIMEOUT", 1.5)
    started = time.perf_counter()
    outcomes = run_many(start_hangs.work, list(range(3)), workers=2, timeout=5.0)
    took = time.perf_counter() - started

    assert [o.index for o in outcomes] == [0, 1, 2]
    assert all(o.error is not None and o.error.startswith("WorkerStartError") for o in outcomes)
    assert "was not ready 1.5 s" in (outcomes[0].error or "")
    assert took < 60.0


def test_a_worker_ended_while_it_waits_is_replaced_and_what_was_collected_is_kept(tmp_path: Path) -> None:
    noted = tmp_path / "busy.txt"
    stopped: list[int] = []

    def kill_the_idle_one() -> None:
        # Item 0 sleeps four seconds in one worker; the other worker has done item 1 and is idle: it is ended from outside.
        deadline = time.monotonic() + 30.0
        while not noted.exists() and time.monotonic() < deadline:
            time.sleep(0.05)
        time.sleep(1.5)
        busy = int(noted.read_text(encoding="ascii"))
        for child in multiprocessing.active_children():
            if child.pid != busy:
                child.terminate()
                stopped.append(child.pid or 0)

    killer = threading.Thread(target=kill_the_idle_one)
    killer.start()
    try:
        outcomes = run_many(work.sleeps, [(index, str(noted)) for index in range(2)], workers=2, timeout=30.0)
    finally:
        killer.join()

    assert stopped
    assert [o.ok for o in outcomes] == [True, True]
    assert [o.value[0] for o in outcomes if o.value is not None] == [0, 1]
