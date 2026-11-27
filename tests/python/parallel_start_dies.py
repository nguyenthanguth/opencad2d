"""A function whose module ends the worker that imports it, before the worker has said it is ready (for the test of a worker that
ends during its start); the process that runs the test imports it unharmed."""

import multiprocessing
import os

if multiprocessing.current_process().name != "MainProcess":  # a spawned worker is named before it unpickles its function
    os._exit(3)


def work(item: int, seed: int) -> int:
    return item
