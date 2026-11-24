"""HelloWorld -- the smallest Python tool: PYHELLOWORLD writes Hello, World! on the command line.

It asks nothing and changes nothing. A Python tool is a folder: this __init__.py, a requirements.txt (empty here, for the
tool needs no package) and a plugin.json that names the command. The app shows PYHELLOWORLD at start and starts the tool's
process the first time it is typed.
"""

from opencad2d.cad import command, write


@command("PYHELLOWORLD")
def hello() -> None:
    write("Hello, World!")
