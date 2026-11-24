OpenCAD2D - Pythons folder
===================================

Put Python tools here, and they are loaded when the app starts. A Python tool is a folder with a
requirements.txt (it may be empty; it names the packages the tool needs, one to a line) and an
__init__.py that defines commands: a function marked @command("NAME") becomes a command, and
cmd("CIRCLE", ...) types a command to the end. A plugin.json in the folder names the commands and the
ribbon buttons the tool adds, so that the app shows them with no Python running; see the Python guide.

What loads at start: every folder directly in this folder that holds a requirements.txt, in the order
of their names, ignoring case. Loading a tool starts no Python and installs nothing. The tool's own
Python process starts the first time one of its commands is typed (about 0.4 s), after the app has
asked to install the packages of its requirements.txt where there are any and they are not installed
yet; each tool has an environment of its own, so two tools may use two versions of one package.

A folder with no requirements.txt is not a tool: it is a package the tools import. A .py file
directly in this folder is not loaded: put it in a folder of its own as __init__.py, with a
requirements.txt. PYRUN still runs a single .py file once.

Two folders here are sample tools that come with the app:

  HelloWorld    its command PYHELLOWORLD writes "Hello, World!": the smallest tool there can be.
  DrawingInfo   its command GHPYINFO writes the name, objects, layers, blocks and extents of the drawing.

Delete the folder of one if you do not want it, or copy one under another name to start a tool of your
own. The sources are also in examples\Pythons of https://github.com/nguyenthanguth/opencad2d.

Starting a tool runs its code with your rights: put here only what you trust.
