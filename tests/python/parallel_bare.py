"""Worker functions that import nothing from ``opencad2d``: what they see of .NET in a worker is what the pool's initializer
loaded, not what importing their own module did."""

import os
import sys
import time


def whose(item: int, seed: int) -> tuple[int, bool]:
    """The process this item ran in, and whether .NET (pythonnet's ``clr``) was already loaded in it."""
    return os.getpid(), "clr" in sys.modules


def dies(item: int, seed: int) -> int:
    """Item 2 ends its worker without a word after a wait; item 5 is still at work then; the others return their number."""
    if item == 2:
        time.sleep(1.5)
        os._exit(1)
    if item == 5:
        time.sleep(4.0)
    return item
