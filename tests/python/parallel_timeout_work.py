"""The functions the tests of the timeout of ``opencad2d.parallel.run_many`` run in worker processes (at the top of a module, which
a spawned worker imports by name)."""

import os
import time


def hangs_on_two(item: int, seed: int) -> tuple[int, int]:
    """Item 2 sleeps a minute; the others return their number and the process they ran in."""
    if item == 2:
        time.sleep(60.0)
    return item, os.getpid()


def noted(item: tuple[int, str], seed: int) -> tuple[int, int]:
    """Item 2 writes the process it runs in to the file ``item[1]`` and sleeps a minute; the others return their number and process."""
    if item[0] == 2:
        with open(item[1], "w", encoding="ascii") as noting:
            noting.write(str(os.getpid()))
        time.sleep(60.0)
    return item[0], os.getpid()


def sleeps(item: tuple[int, str], seed: int) -> tuple[int, int]:
    """Item 0 writes its process to the file ``item[1]`` and sleeps four seconds; the others return at once."""
    if item[0] == 0:
        with open(item[1], "w", encoding="ascii") as noting:
            noting.write(str(os.getpid()))
        time.sleep(4.0)
    return item[0], os.getpid()
