import pytest
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


@pytest.fixture(scope="session")
def live_mock_server():
    """Starts the mock server for integration tests and stops it afterwards."""
    server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
    server.start()
    yield server
    server.stop()
