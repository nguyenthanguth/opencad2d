"""The functions the tests of ``opencad2d.parallel`` run in worker processes: a spawned worker imports them by module name,
so they live at the top of a module and not in a test."""

import hashlib
import random
import time
from typing import Any

from opencad2d import synth


def plate(item: tuple[float, float], seed: int) -> tuple[str, float, int]:
    """The ground truth of one synthetic plate of crowding ``item[0]``, as its JSON's hash, its score and its overlaps; the
    process waits ``item[1]`` seconds first, so that the items finish in another order than they started."""
    time.sleep(item[1])
    sample = synth.generate(seed, item[0])
    return hashlib.sha256(sample.json.encode("utf-8")).hexdigest(), round(sample.score, 9), sample.overlaps


def flaky(item: int, seed: int) -> int:
    """The number drawn from the seed, scaled by ``item``; item 3 raises."""
    if item == 3:
        raise ValueError("item 3 is bad")
    return int(random.Random(seed).random() * 1_000_000) * item


def unpicklable(item: int, seed: int) -> Any:
    """Item 1 returns a value that cannot travel back from the worker; the others return their number."""
    return (lambda: item) if item == 1 else item
