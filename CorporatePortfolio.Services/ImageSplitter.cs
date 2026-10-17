using ImageMagick;

namespace CorporatePortfolio.Services
{
    /// <summary>
    /// Used for splitting the photos out of the headshot_grid images.
    /// </summary>
    public class ImageSplitter
    {
        public static async Task<List<MagickImage>> SplitGridImage(string imagePath, int totalColumns, int totalRows)
        {
            using var masterImage = new MagickImage(imagePath);

            int subImageWidth = (int)(masterImage.Width / totalColumns);
            int subImageHeight = (int)(masterImage.Height / totalRows);

            var separatePhotosCollection = new List<MagickImage>();

            for (int row = 0; row < totalRows; row++)
            {
                for (int col = 0; col < totalColumns; col++)
                {
                    int x = col * subImageWidth;
                    int y = row * subImageHeight;

                    var singlePortrait = new MagickImage(masterImage);

                    var cropArea = new MagickGeometry(x, y, (uint)subImageWidth, (uint)subImageHeight);

                    singlePortrait.Crop(cropArea);

                    separatePhotosCollection.Add(singlePortrait);
                }
            }

            return separatePhotosCollection;
        }
    }
}