"""A function whose module hangs the worker that imports it, before the worker has said it is ready (for the test of the
start-up deadline); the process that runs the test imports it unharmed."""

import multiprocessing
import time

if multiprocessing.current_process().name != "MainProcess":  # a spawned worker is named before it unpickles its function
    time.sleep(600.0)


def work(item: int, seed: int) -> int:
    return item
