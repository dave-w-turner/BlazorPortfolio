using ImageMagick;

namespace CorporatePortfolio.Services
{
    public class ImageSplitter
    {
        public static async Task<List<MagickImage>> SplitGridImage(string imagePath, int totalColumns, int totalRows)
        {
            // 1. Load the master grid image
            using var masterImage = new MagickImage(imagePath);

            // 2. Dynamically calculate exact dimensions
            int subImageWidth = (int)(masterImage.Width / totalColumns);
            int subImageHeight = (int)(masterImage.Height / totalRows);

            var separatePhotosCollection = new List<MagickImage>();

            // 3. Loop and crop each portrait out
            for (int row = 0; row < totalRows; row++)
            {
                for (int col = 0; col < totalColumns; col++)
                {
                    int x = col * subImageWidth;
                    int y = row * subImageHeight;

                    // Create a clone of the master to crop specifically 
                    var singlePortrait = new MagickImage(masterImage);

                    // Define the bounding box area
                    var cropArea = new MagickGeometry(x, y, (uint)subImageWidth, (uint)subImageHeight);

                    // Perform the crop operation
                    singlePortrait.Crop(cropArea);

                    separatePhotosCollection.Add(singlePortrait);
                }
            }

            return separatePhotosCollection;
        }
    }
}