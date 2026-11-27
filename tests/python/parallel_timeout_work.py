"""The functions the tests of the timeout of ``opencad2d.parallel.run_many`` run in worker processes (at the top of a module, which
a spawned worker imports by name)."""

import os
import time


def hangs_on_two(item: int, seed: int) -> tuple[int, int]:
    """Item 2 sleeps a minute; the others return their number and the process they ran in."""
    if item == 2:
        time.sleep(60.0)
    return item, os.getpid()
