"""Client security regressions using a local HTTP fixture; no game is contacted."""
import importlib.util
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import threading
import unittest

spec = importlib.util.spec_from_file_location(
    "repo_api", Path(__file__).resolve().parents[1] / "examples/python/repo_api.py"
)
client = importlib.util.module_from_spec(spec)
spec.loader.exec_module(client)


class ClientTests(unittest.TestCase):
    def test_redirects_never_receive_bearer_credentials(self):
        received = []

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                received.append(self.path)
                self.send_response(302)
                self.send_header("Location", "/capture")
                self.end_headers()

            def log_message(self, *args):
                pass

        with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
            worker = threading.Thread(target=server.serve_forever, daemon=True)
            worker.start()
            try:
                api = client.RepoApi(token="test-only-token", base_url=f"http://127.0.0.1:{server.server_port}")
                with self.assertRaises(client.ApiError) as error:
                    api.get("/health")
                self.assertEqual(302, error.exception.status)
                self.assertEqual(["/health"], received)
            finally:
                server.shutdown()
                worker.join()


if __name__ == "__main__":
    unittest.main()
