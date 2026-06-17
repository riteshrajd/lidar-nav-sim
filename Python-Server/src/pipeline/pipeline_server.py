import os
import sys
import time
from http.server import HTTPServer, BaseHTTPRequestHandler

# Import pipeline steps (must be run from the root directory ideally, or these modules must be in path)
sys.path.append(os.path.join(os.path.dirname(__file__), ''))
from main_pipeline import run_pipeline

# ==============================================================================
# CONFIGURATION
# ==============================================================================
# Define the new pipeline media directories
SAVE_DIR = "src/pipeline/media/input/raw"

class PipelineServerHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        os.makedirs(SAVE_DIR, exist_ok=True)
        
        content_length = int(self.headers.get('Content-Length', 0))
        if content_length == 0:
            self.send_response(400)
            self.end_headers()
            return
            
        image_data = self.rfile.read(content_length)
        
        # Unity sends direction in header. If not provided, assume 'capture'
        direction = self.headers.get('Direction', 'capture').lower()
        capture_time = self.headers.get('Capture-Time', time.strftime("%Y%m%d_%H%M%S"))
        
        filename = f"{capture_time}_{direction}.png"
        filepath = os.path.join(SAVE_DIR, filename)
        
        with open(filepath, 'wb') as f:
            f.write(image_data)
            
        print(f"[{time.strftime('%H:%M:%S')}] Saved [{direction}] view to {filepath}")
        
        # Process pipeline ONLY for the front image as requested
        if direction == 'front':
            print(f"[{time.strftime('%H:%M:%S')}] Triggering VLM Pipeline for 'front' view...")
            # run_pipeline now returns the VLM JSON response
            vlm_json = run_pipeline(image_path=filepath, output_dir_base="src/pipeline/media")
            
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            self.wfile.write(vlm_json.encode('utf-8'))
        else:
            # For non-front images, just acknowledge receipt
            self.send_response(200)
            self.send_header('Content-type', 'text/plain')
            self.end_headers()
            self.wfile.write(b"Image received and saved.")

if __name__ == '__main__':
    port = 8000
    server_address = ('', port)
    httpd = HTTPServer(server_address, PipelineServerHandler)
    
    os.makedirs(SAVE_DIR, exist_ok=True)
    
    print(f"Pipeline Server running on http://localhost:{port}")
    print(f"Saving ALL captures to:     {SAVE_DIR}")
    print("\nWaiting for 'front' capture from Unity to trigger the vision pipeline...")
    
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down server.")
        httpd.server_close()
