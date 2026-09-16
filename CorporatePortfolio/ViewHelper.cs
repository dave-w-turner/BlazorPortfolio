using ImageMagick;

namespace CorporatePortfolio
{
    public class ViewHelper
    {
        public static string GetRandomBase64HeadShot()
        {
            var filePaths = Directory.EnumerateFiles("wwwroot/images/headshots")
                                     .ToList();

            if (filePaths.Count == 0)
                return "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=";

            int randomIndex = Random.Shared.Next(filePaths.Count);
            string chosenPath = filePaths[randomIndex];
            byte[] imageBytes;

            // Explicitly selecting your provided master grid
            chosenPath = filePaths.FirstOrDefault(p => p.Contains("headshot_grid_1")) ?? string.Empty;

            if (chosenPath.Contains("headshot_grid_1") || chosenPath.Contains("headshot_grid_2"))
            {
                int maxRows = 0, maxCols = 0;
                int topOffset = 0;
                int bottomOffset = 0;
                int shavePixels = 0;
                bool trimWhiteBorders = false;

                if (chosenPath.Contains("headshot_grid_1"))
                {
                    maxRows = 4;
                    maxCols = 3;
                    topOffset = 0;
                    bottomOffset = 0;
                    shavePixels = 10;
                    trimWhiteBorders = false;
                }
                else if (chosenPath.Contains("headshot_grid_2"))
                {
                    maxRows = 3;
                    maxCols = 4;
                    topOffset = 0;
                    bottomOffset = 0;
                    shavePixels = 0;
                    trimWhiteBorders = true;
                }

                imageBytes = GetRandomBase64TailShot(chosenPath, Random.Shared.Next(maxCols), Random.Shared.Next(maxRows), maxCols, maxRows, topOffset, bottomOffset, shavePixels, trimWhiteBorders);

                if (imageBytes == null || imageBytes.Length == 0)
                    return "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=";
            }
            else
            {
                using var image = new MagickImage(chosenPath);

                image.BackgroundColor = MagickColors.White;
                image.Format = MagickFormat.Jpeg;
                imageBytes = image.ToByteArray();
            }

            return Convert.ToBase64String(imageBytes);
        }

        private static byte[] GetRandomBase64TailShot(string imagePath, int targetColumn, int targetRow, int totalColumns, int totalRows, int topOffset, int bottomOffset, int shavePixels, bool trimWhiteBorders)
        {
            byte[] imageBytes;

            using (var gridImage = new MagickImage(imagePath))
            {
                int usableHeight = (int)gridImage.Height - topOffset - bottomOffset;

                double cellWidth = (double)gridImage.Width / totalColumns;
                double cellHeight = (double)usableHeight / totalRows;

                int xStart = (int)Math.Round(targetColumn * cellWidth);
                int xEnd = (int)Math.Round((targetColumn + 1) * cellWidth);

                int yStart = topOffset + (int)Math.Round(targetRow * cellHeight);
                int yEnd = topOffset + (int)Math.Round((targetRow + 1) * cellHeight);

                int width = xEnd - xStart;
                int height = yEnd - yStart;

                if (!trimWhiteBorders)
                {
                    if (targetRow > 0)
                    {
                        yStart += shavePixels;
                        height -= shavePixels;
                    }

                    if (targetRow < totalRows - 1)
                    {
                        height -= shavePixels;
                    }
                }

                var geometry = new MagickGeometry(xStart, yStart, (uint)width, (uint)height);
                gridImage.Crop(geometry);
                gridImage.ResetPage();

                if (trimWhiteBorders)
                {
                    gridImage.ColorFuzz = new Percentage(20);
                    gridImage.Trim();
                    gridImage.ResetPage();
                }

                // CRITICAL FOR GRID EXTRACTS: Force background color color-space flattening 
                // This strips out any temporary transparency alpha flags before writing the raw JPEG stream
                gridImage.BackgroundColor = trimWhiteBorders ? MagickColors.White : MagickColors.Black;

                // Use Extent/Flatten behaviors to cleanly finalize the pixel buffer map
                gridImage.Extent(gridImage.Width, gridImage.Height);

                imageBytes = gridImage.ToByteArray(MagickFormat.Jpeg);
            }

            return imageBytes;
        }
    }
}
