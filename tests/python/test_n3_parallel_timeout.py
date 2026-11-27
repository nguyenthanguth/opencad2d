"""N3: the timeout of ``opencad2d.parallel.run_many``: an item that runs over it is a timed-out error, its worker is replaced, the
other items go on and the results stay in the order of the items."""

import time

import pytest

import opencad2d  # noqa: F401  (boots .NET and loads the assemblies)
from opencad2d.parallel import run_many

import parallel_timeout_work as work


@pytest.mark.parametrize("workers", [1, 2])
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
