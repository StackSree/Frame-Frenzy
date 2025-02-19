
using Microsoft.Extensions.Logging;

namespace Snapster;
internal class Program
{
    public static async Task Main(string[] args)
    {
        string inputLocation = args.Length > 0 ? args[0] : "./Input";
        string outputLocation = args.Length > 1 ? args[1] : "./Output";

        Directory.CreateDirectory(inputLocation);
        Directory.CreateDirectory(outputLocation);

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddHostedService(provider => new ImageProcessorService(inputLocation, outputLocation));
            })
            .Build();

        await host.RunAsync();
    }
}


public class ImageProcessorService : BackgroundService
{
    private readonly string _inputLocation;
    private readonly string _outputLocation;
    private readonly ILogger<ImageProcessorService> _logger;

    public ImageProcessorService(string inputLocation, string outputLocation)
    {
        _inputLocation = inputLocation;
        _outputLocation = outputLocation;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger?.LogInformation("Image Processor Service is starting...");

        using var watcher = new FileSystemWatcher(_inputLocation, "*.BMP")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        watcher.Created += async (s, e) => await ProcessImageAsync(new FileInfo(e.FullPath));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken); // Keeps the service alive
        }

        _logger?.LogInformation("Image Processor Service is stopping...");
    }

    private async Task ProcessImageAsync(FileInfo file)
    {
        try
        {
            _logger?.LogInformation($"Processing: {file.Name} ({file.Length} bytes)");

            using (Mat image = Cv2.ImRead(file.FullName, ImreadModes.Color))
            {
                if (image.Empty())
                {
                    _logger?.LogError($"Error: Could not load image {file.Name}");
                    return;
                }

                int width = image.Width;
                int height = image.Height;

                string outputDir = Path.Combine(_outputLocation, Path.GetFileNameWithoutExtension(file.Name));
                Directory.CreateDirectory(outputDir);

                using (Mat grayImage = new Mat())
                using (Mat resizedImage = new Mat())
                using (Mat edgeImage = new Mat())
                {
                    Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
                    string grayPath = Path.Combine(outputDir, "gray.png");
                    Cv2.ImWrite(grayPath, grayImage);

                    Cv2.Resize(image, resizedImage, new Size(width / 2, height / 2));
                    string resizedPath = Path.Combine(outputDir, "resized.png");
                    Cv2.ImWrite(resizedPath, resizedImage);

                    Cv2.Canny(grayImage, edgeImage, 100, 200);
                    string edgePath = Path.Combine(outputDir, "edges.png");
                    Cv2.ImWrite(edgePath, edgeImage);

                    SaveImageMetadata(file, width, height, grayPath, resizedPath, edgePath, outputDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error processing image {file.Name}: {ex.Message}");
        }
    }



    private void SaveImageMetadata(FileInfo file, int width, int height, string grayPath, string resizedPath, string edgePath, string outputDir)
    {
        var metadata = new
        {
            OriginalFile = file.FullName,
            SizeBytes = file.Length,
            Dimensions = new { Width = width, Height = height },
            Outputs = new
            {
                Grayscale = grayPath,
                Resized = resizedPath,
                Edges = edgePath
            }
        };

        string metadataPath = Path.Combine(outputDir, "metadata.json");
        File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

        _logger?.LogInformation($"Metadata saved: {metadataPath}");
    }
}
