"""S1: ``opencad2d.parallel.run_many`` (PW7.8): the same results for any number of workers, in the order of the items, an item
that raises reported while the others finish, each worker loading .NET once.

The items are six plates of ``opencad2d.synth`` of crowding 0 to 1, built in code; a worker waits longer for the early items, so
the last item finishes first.
"""

import hashlib
import os
import time

import pytest

import opencad2d  # noqa: F401  (boots .NET and loads the assemblies)
from opencad2d.parallel import Outcome, item_seed, run_many

import parallel_scenarios as work

ITEMS = [(step / 5.0, (5 - step) * 0.15) for step in range(6)]


def test_the_seed_of_an_item_is_a_function_of_the_run_and_its_place_alone() -> None:
    seeds = [item_seed(42, index) for index in range(100)]

    assert seeds == [item_seed(42, index) for index in range(100)]
    assert len(set(seeds)) == 100
    assert set(seeds).isdisjoint(item_seed(43, index) for index in range(100))
    assert all(0 <= seed < 2**63 for seed in seeds)
    assert item_seed(42, 0) == int.from_bytes(hashlib.sha256(b"opencad2d:42:0").digest()[:8], "big") >> 1


def test_one_worker_and_three_give_the_same_values_in_the_order_of_the_items() -> None:
    started = time.perf_counter()
    one = run_many(work.plate, ITEMS, workers=1, seed=42)
    between = time.perf_counter()
    three = run_many(work.plate, ITEMS, workers=3, seed=42)
    finished = time.perf_counter()
    print(f"\nrun_many of 6 plates: workers=1 {between - started:.2f} s, workers=3 {finished - between:.2f} s")

    assert [o.index for o in three] == list(range(6))
    assert [o.value for o in one] == [o.value for o in three]
    assert [o.seed for o in one] == [o.seed for o in three] == [item_seed(42, i) for i in range(6)]
    assert all(o.ok and o.error is None and o.seconds > 0.0 for o in one + three)
    assert len({o.value for o in three}) == 6  # six different plates
    assert finished - between < 60.0


def test_another_seed_of_the_run_gives_other_plates() -> None:
    first = run_many(work.plate, ITEMS[:2], workers=1, seed=1)
    second = run_many(work.plate, ITEMS[:2], workers=1, seed=2)

    assert [o.value for o in first] != [o.value for o in second]


def test_an_item_that_raises_is_reported_and_the_others_finish() -> None:
    outcomes = run_many(work.flaky, [1, 2, 3, 4, 5], workers=3, seed=7)

    assert [o.ok for o in outcomes] == [True, True, False, True, True]
    bad = outcomes[2]
    assert bad.value is None
    assert bad.error is not None and bad.error.splitlines()[-1] == "ValueError: item 3 is bad"
    inline = run_many(work.flaky, [1, 2, 3, 4, 5], workers=1, seed=7)
    assert [o.value for o in outcomes] == [o.value for o in inline]
    assert [o.error is None for o in inline] == [o.ok for o in outcomes]


def test_a_value_that_cannot_travel_back_is_that_items_error_and_the_pool_goes_on() -> None:
    outcomes = run_many(work.unpicklable, [0, 1, 2, 3], workers=2)

    assert [o.ok for o in outcomes] == [True, False, True, True]
    assert [o.value for o in outcomes if o.ok] == [0, 2, 3]
    assert outcomes[1].error is not None and outcomes[1].index == 1


def test_each_worker_has_dotnet_loaded_before_its_first_item_and_there_are_no_more_workers_than_asked() -> None:
    outcomes = run_many(work.whose, list(range(8)), workers=2)

    values = [o.value for o in outcomes if o.value is not None]
    assert len(values) == 8
    assert all(loaded for _, loaded in values)
    assert 1 <= len({pid for pid, _ in values}) <= 2
    assert os.getpid() not in {pid for pid, _ in values}


def test_one_worker_runs_in_the_callers_process() -> None:
    (outcome,) = run_many(work.whose, [0], workers=1)

    assert outcome.value is not None and outcome.value[0] == os.getpid()


def test_no_items_give_an_empty_list_and_a_number_of_workers_below_one_is_refused() -> None:
    assert run_many(work.flaky, [], workers=4) == []
    with pytest.raises(ValueError, match="workers is 1 or more"):
        run_many(work.flaky, [1], workers=0)


def test_an_outcome_says_whether_it_is_ok() -> None:
    assert Outcome(0, 1, 5, None, 0.1).ok
    assert not Outcome(0, 1, None, "boom", 0.1).ok
