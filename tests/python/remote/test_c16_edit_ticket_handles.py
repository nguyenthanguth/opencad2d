"""C16: the handles of what is queued with an edit call, through the app (A8 follow-up): the commit's answer carries the handles
of the inserts sent with it, so a script that queues N inserts and calls ``copy`` makes one request, not 1 + N: the backend
reads the handles from the tickets of the document (``CadRemoteDocument.HandleOfTicket``) and never asks ``GetObject`` for them.
"""

import time
from collections.abc import Iterator
from typing import Any

import pytest

import edit_scenarios
from edit_scenarios import Twins, make_twins


class _CountingDocument:
    """The ``CadRemoteDocument`` of a backend with the calls of ``GetObject`` counted; everything else goes through."""

    def __init__(self, document: Any) -> None:
        self._document = document
        self.reads = 0

    def GetObject(self, handle: str) -> Any:  # noqa: N802 - the .NET name
        self.reads += 1
        return self._document.GetObject(handle)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._document, name)


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    from opencad2d.cad import App

    with App(app) as made:
        yield made


@pytest.fixture
def twins(cad_app: Any) -> Twins:
    return make_twins(cad_app.new(), cad_app.new())


def _counting(drawing: Any) -> _CountingDocument:
    backend = drawing._backend  # pyright: ignore[reportPrivateUsage]
    counting = _CountingDocument(backend._document)
    backend._document = counting
    return counting


def test_the_inserts_queued_with_a_copy_get_their_handles_without_a_read_of_each(twins: Twins) -> None:
    drawing = twins.called
    counting = _counting(drawing)
    added = [drawing.add_line((i, 100), (i, 150)) for i in range(20)]

    copies = drawing.copy([twins.plan["line"]], (0, 50))

    # Twenty inserts and a copy are one commit; the inserts' handles are real and each names the line queued, in order.
    assert len(copies) == 1
    assert counting.reads == 0
    assert all(not entity.handle.startswith("~") for entity in added)
    assert len({entity.handle for entity in added}) == 20
    assert counting.reads == 0
    for index, entity in enumerate(added):
        assert drawing.entity(entity.handle).net.StartPoint.X == index


@pytest.mark.timing
def test_a_thousand_inserts_and_a_copy_are_one_request_and_take_a_fraction_of_a_second(twins: Twins) -> None:
    drawing = twins.called
    counting = _counting(drawing)
    started = time.perf_counter()

    for i in range(1000):
        drawing.add_line((i, 100), (i, 150))
    queued = time.perf_counter()
    drawing.copy([twins.plan["line"]], (0, 50))
    done = time.perf_counter()

    print(f"1000 add_line: {queued - started:.3f} s; copy with the commit and the handles: {done - queued:.3f} s")
    assert counting.reads == 0
    assert len(edit_scenarios.snapshot(drawing)) == len(edit_scenarios.snapshot(twins.typed)) + 1001
    assert done - queued < 30.0
