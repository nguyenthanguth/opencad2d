"""C17 (PW4): a ``CadApplication`` used after ``Dispose`` raises ``System.ObjectDisposedException`` and opens no new pipe, where
it once reconnected without a word; a second ``Dispose`` is harmless."""

from typing import Any

import pytest
from System import ObjectDisposedException


def test_a_call_after_dispose_raises_object_disposed_and_says_what_to_do(app: Any, drawing: Any) -> None:
    assert drawing.Name

    app.Dispose()

    with pytest.raises(ObjectDisposedException) as raised:
        list(app.Documents)
    assert "make a new CadApplication" in str(raised.value)
    with pytest.raises(ObjectDisposedException):
        drawing.GetObjects()


def test_a_second_dispose_does_nothing(app: Any) -> None:
    app.Dispose()

    app.Dispose()
