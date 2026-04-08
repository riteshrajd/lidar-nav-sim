import os
import time
from http.server import HTTPServer, BaseHTTPRequestHandler

# Define the folder path
SAVE_DIR = "media/visioncapture"

class VisionServerHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        # Create media directory if it doesn't exist
        os.makedirs(SAVE_DIR, exist_ok=True)
        
        # Determine content length
        content_length = int(self.headers.get('Content-Length', 0))
        if content_length == 0:
            self.send_response(400)
            self.end_headers()
            return
            
        # Read the raw image bytes
        image_data = self.rfile.read(content_length)
        
        # Extract custom headers sent from Unity
        direction = self.headers.get('Direction', 'capture')
        capture_time = self.headers.get('Capture-Time', time.strftime("%Y%m%d_%H%M%S"))
        
        filename = f"{capture_time}_{direction}.png"
        filepath = os.path.join(SAVE_DIR, filename)
        
        # Save file to disk
        with open(filepath, 'wb') as f:
            f.write(image_data)
            
        print(f"[{time.strftime('%H:%M:%S')}] Saved image to {filepath}")
        
        # Send a success response
        self.send_response(200)
        self.send_header('Content-type', 'text/plain')
        self.end_headers()
        self.wfile.write(b"Image saved successfully by Python Server!")

if __name__ == '__main__':
    port = 8000
    server_address = ('', port)
    httpd = HTTPServer(server_address, VisionServerHandler)
    
    # Ensure starting directory logic matches the requested save folder location
    current_dir = os.path.dirname(os.path.abspath(__file__))
    os.chdir(current_dir)
    os.makedirs(SAVE_DIR, exist_ok=True)
    
    print(f"Vision Capture Server running on http://localhost:{port}")
    print(f"Saving images to: {os.path.join(current_dir, SAVE_DIR)}")
    print("Waiting for captures from Unity...")
    httpd.serve_forever()
