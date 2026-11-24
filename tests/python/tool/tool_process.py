"""A tool's process and its control pipes, as the tool suite drives it."""

import json
import os
import queue
import subprocess
import threading
import time
from typing import Any

import pytest

LATE = 20.0


class ToolProcess:
    """A tool's process and its control pipes, with the lines it wrote on them and on its standard streams."""

    def __init__(self, process: "subprocess.Popen[str]", to_tool: int, from_tool: int) -> None:
        self.process = process
        self.to_tool = os.fdopen(to_tool, "wb", buffering=0)
        self._messages: "queue.Queue[dict[str, Any] | None]" = queue.Queue()
        self.stdout: list[str] = []
        self.stderr: list[str] = []
        reader = os.fdopen(from_tool, "rb")
        threading.Thread(target=self._pump_control, args=(reader,), daemon=True).start()
        threading.Thread(target=self._pump_text, args=(process.stdout, self.stdout), daemon=True).start()
        threading.Thread(target=self._pump_text, args=(process.stderr, self.stderr), daemon=True).start()

    def _pump_control(self, reader: Any) -> None:
        for line in reader:
            self._messages.put(json.loads(line.decode("utf-8")))
        self._messages.put(None)

    @staticmethod
    def _pump_text(stream: Any, lines: list[str]) -> None:
        for line in stream:
            lines.append(line.rstrip("\r\n"))

    def send(self, message: dict[str, Any]) -> None:
        """Writes one control message, as the app does."""
        self.to_tool.write((json.dumps(message) + "\n").encode("utf-8"))

    def receive(self, op: str, timeout: float = LATE) -> dict[str, Any]:
        """Waits for the next control message, which must be ``op``."""
        try:
            message = self._messages.get(timeout=timeout)
        except queue.Empty:
            pytest.fail(f"The tool said no {op} within {timeout} s. stderr: {self.stderr}")
        if message is None:
            pytest.fail(
                f"The tool ended (exit code {self.process.poll()}) instead of saying {op}. stderr: {self.stderr}"
            )
        assert message["op"] == op, message
        return message

    def ended_within(self, seconds: float) -> float:
        """Waits for the process to exit and gives the seconds it took; fails after ``seconds``."""
        start = time.perf_counter()
        try:
            self.process.wait(timeout=seconds)
        except subprocess.TimeoutExpired:
            pytest.fail(f"The tool process was still running after {seconds} s.")
        return time.perf_counter() - start
