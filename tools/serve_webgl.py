"""Serves the WebGL build over the local network so a phone or tablet on the same Wi-Fi can open it.

Usage: python tools/serve_webgl.py [port] [folder]      (defaults: 8765, Builds/WebGL)

Prints the addresses to type into the phone's browser. Responses are never cached, so a rebuilt game is picked up on reload.
Plain http:// on a LAN address is not a "secure context", so the browser will not install the service worker there;
the game still plays, it just does not cache itself for offline use. Host it over https (e.g. GitHub Pages) for that.
"""
import functools
import http.server
import os
import socket
import sys


class NoCacheHandler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map,
                      ".wasm": "application/wasm", ".webmanifest": "application/manifest+json", ".unityweb": "application/octet-stream"}

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


def lan_addresses():
    found = set()
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("10.255.255.255", 1))          # no packet is sent; this just picks the outbound interface
        found.add(s.getsockname()[0])
        s.close()
    except OSError:
        pass
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            found.add(info[4][0])
    except OSError:
        pass
    return sorted(a for a in found if not a.startswith("127.") and not a.startswith("169.254."))


def main():
    sys.stdout.reconfigure(line_buffering=True)
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    root = os.path.abspath(sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "..", "Builds", "WebGL"))
    if not os.path.isfile(os.path.join(root, "index.html")):
        sys.exit("No index.html in " + root + ". Build the WebGL player first (task: Build WebGL player).")
    handler = functools.partial(NoCacheHandler, directory=root)
    server = http.server.ThreadingHTTPServer(("0.0.0.0", port), handler)
    print("Serving " + root)
    print("  this PC:  http://localhost:%d/" % port)
    for a in lan_addresses():
        print("  network:  http://%s:%d/" % (a, port))
    print("Ctrl+C to stop. If the phone can't connect, allow Python through Windows Firewall (private networks).")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")


if __name__ == "__main__":
    main()
