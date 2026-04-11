import os
from PIL import Image, ImageDraw, ImageFont

def add_coordinate_grid(image_path, output_dir="media/input/grid", grid_size=10):
    os.makedirs(output_dir, exist_ok=True)
    image = Image.open(image_path).convert("RGB")
    draw = ImageDraw.Draw(image)
    width, height = image.size
    
    try:
        # Smaller font so intersections don't become too cluttered
        font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 20)
    except:
        font = ImageFont.load_default()
    
    grid_color = (0, 255, 0, 150)
    
    # Draw Grid Lines
    for i in range(grid_size + 1):
        x = int(i * width / grid_size)
        y = int(i * height / grid_size)
        draw.line([(x, 0), (x, height)], fill=grid_color, width=2)  # Vertical
        draw.line([(0, y), (width, y)], fill=grid_color, width=2)  # Horizontal
        
    # Draw (x,y) Coordinate Labels at Every Intersection
    for x_idx in range(grid_size + 1):
        for y_idx in range(grid_size + 1):
            px = int(x_idx * width / grid_size)
            py = int(y_idx * height / grid_size)
            
            label = f"({x_idx},{y_idx})"
            # Offset text slightly (4px) to the bottom-right of the exact intersection
            draw.text((px + 4, py + 4), label, fill="yellow", font=font, stroke_width=2, stroke_fill="black")
    
    base_name = os.path.basename(image_path)
    out_path = os.path.join(output_dir, f"coord_gridded_{base_name}")
    
    image.save(out_path)
    print(f"✅ Coordinate gridded image saved to: {out_path}")
    
    return out_path

if __name__ == "__main__":
    IMAGE_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/image_input_6.png"
    add_coordinate_grid(IMAGE_PATH, grid_size=10)