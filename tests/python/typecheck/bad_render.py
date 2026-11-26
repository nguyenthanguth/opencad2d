"""Type-checks with exactly the errors marked by an expect comment on their lines: what a script gets wrong with the render
calls of a drawing (PW6.1)."""

from opencad2d.cad import new_file

drawing = new_file()

# the ground is black or white
drawing.render_png("plan.png", ground="grey")  # expect: reportArgumentType

# a size is a whole number of pixels
drawing.render_png("plan.png", width=800.5)  # expect: reportArgumentType

# the pixels are unsigned bytes, not doubles
pixels: list[float] = drawing.render_image()  # expect: reportAssignmentType

# render_pdf plots a layout: it has no size
drawing.render_pdf("plan.pdf", width=800)  # expect: reportCallIssue

# a picture goes to a file named by a string
drawing.render_png(42)  # expect: reportArgumentType
