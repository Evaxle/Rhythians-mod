#pragma once

typedef struct { float x, y; } Vector;
typedef struct { float x, y, width, height; } Box;
typedef struct { unsigned char r, g, b, a; } Tint;
typedef struct { unsigned int id; int width, height, mipmaps, format; } Texture;
typedef struct { int size, count, padding; Texture texture; void *rects, *glyphs; } Font;
