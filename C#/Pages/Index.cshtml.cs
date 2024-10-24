using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.AI.OpenAI;
using Azure.AI.Vision.ImageAnalysis;
using OpenAI.Chat;
using Microsoft.CognitiveServices.Speech;
using System.Reflection.Metadata;

namespace AITreasureHunt.Pages;

public class IndexModel : PageModel
{
    // Hackathon step 1: Change the default hunter name to your name
    public string HunterName { get; set; } = "Hunter X";
    private readonly ILogger<IndexModel> _logger;

    // App settings
    private string? _aoaiURL, _aoaiDeployment;
    private string? _cognitiveURL, _cognitiveRegion;
    private string? _akvURL, _storageURL, _storageContainer;
    private string? _spTenantId, _spClientId, _spClientSecret;

    // Key Vault secret name
    private const string SECRET_AOAI_KEY = "AOAI-KEY";
    private const string SECRET_COGNITIVE_KEY = "COGNITIVE-KEY";

    // Voice name for Text to Speech
    private const string VOICE_NAME = "en-US-AvaNeural";

    // Images to display
    public List<byte[]> Images { get; set; } = new List<byte[]>();
    public List<string> ImageNames { get; set; } = new List<string>();

    [BindProperty]
    public string SelectedImageName { get; set; }

    private ClientSecretCredential _credential;
    private BlobServiceClient _storageClient;
    private BlobContainerClient _containerClient;

    public IndexModel(IConfiguration configuration, ILogger<IndexModel> logger)
    {
        _logger = logger;

        // Load app settings
        _aoaiURL = configuration["AOAI_URL"];
        _aoaiDeployment = configuration["AOAI_DEPLOYMENT"];
        _cognitiveURL = configuration["COGNITIVE_URL"];
        _cognitiveRegion = configuration["COGNITIVE_REGION"];
        _akvURL = configuration["AKV_URL"];
        _storageURL = configuration["STORAGE_URL"];
        _storageContainer = configuration["STORAGE_CONTAINER"];
        _spTenantId = configuration["SP_TENANT_ID"];
        _spClientId = configuration["SP_CLIENT_ID"];
        _spClientSecret = configuration["SP_CLIENT_SECRET"];
        
        // Initialize data for accessing Azure resource
        _credential = new ClientSecretCredential(_spTenantId, _spClientId, _spClientSecret);
        _storageClient = new BlobServiceClient(new Uri(_storageURL), _credential);
        _containerClient = _storageClient.GetBlobContainerClient(_storageContainer);
    }

    public async Task OnGetAsync()
    {
        System.Console.WriteLine("OnGetAsync called");

        // Retrieve images from storage account
        // This implementation is for limited number of images only
        await foreach (var blobItem in _containerClient.GetBlobsAsync())
        {
            var blobClient = _containerClient.GetBlobClient(blobItem.Name);
            System.Console.WriteLine("Downloading image: " + blobItem.Name);
            var response = await blobClient.DownloadContentAsync();
            Images.Add(response.Value.Content.ToArray());
            ImageNames.Add(blobItem.Name);
        }
        _logger.LogInformation("Images count: " + Images.Count);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        System.Console.WriteLine("OnPostAsync called. SelectedImageName: " + SelectedImageName);

        try
        {
            // Hackathon step 2: Retrieve AOAI and Cognitive API keys from Key Vault
            // Use the secret name constants defined in the class
            var secretClient = new SecretClient(new Uri(_akvURL), _credential);
            var aoaiKey = secretClient.GetSecret(SECRET_AOAI_KEY).Value.Value;
            var cognitiveKey = secretClient.GetSecret(SECRET_COGNITIVE_KEY).Value.Value;

            // Get image content
            var blobClient = _containerClient.GetBlobClient(SelectedImageName);
            var blobResponse = await blobClient.DownloadContentAsync();
            BinaryData imgData = blobResponse.Value.Content;

            // Hackathon step 3: Extract text from the selected image
            ImageAnalysisClient imageClient = new ImageAnalysisClient(
                new Uri(_cognitiveURL), new AzureKeyCredential(cognitiveKey));
            ImageAnalysisResult result = imageClient.Analyze(
                imgData, VisualFeatures.Read);
            var imgText = "";
            if(result.Read != null)
            {
                foreach(var line in result.Read.Blocks.SelectMany(block => block.Lines))
                {
                    imgText += line.Text + " ";
                }
            }
            _logger.LogInformation("Extracted text: " + imgText);

            // Hackathon step 4: Analyze the text and find the hidden info
            // Assign the result to variable: aoaiResponse
            string aoaiResponse = $"What is hidden in the image {SelectedImageName}?";
            var aoaiClient = new AzureOpenAIClient(new Uri(_aoaiURL), 
                new AzureKeyCredential(aoaiKey));
            var chatClient = aoaiClient.GetChatClient(_aoaiDeployment);
            var sysMsg = "You are a helpful assistant that reveals the secret from the message.";
            var userMsg = "Analyze the following text and discover the hidden info:\n" + imgText;
            ChatCompletionOptions options = new() {
                MaxTokens = 2048,
                Temperature = 0.7f};
            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(sysMsg), 
                    new UserChatMessage(userMsg)
                ], options);
            aoaiResponse = completion.Content[0].Text;
            _logger.LogInformation("AOAI response: " + aoaiResponse);

            // Hackathon step 5: Convert the result to speech
            // Use the same cognitive service for speech
            // Convert the speech to Base64 string, and assign to variable: speechBase64
            var speechBase64 = "";
            var speechConfig = SpeechConfig.FromSubscription(cognitiveKey, _cognitiveRegion);
            speechConfig.SpeechSynthesisVoiceName = VOICE_NAME;
            var synthesizer = new SpeechSynthesizer(speechConfig, null);
            var speechResult = await synthesizer.SpeakTextAsync(aoaiResponse);
            switch(speechResult.Reason)
            {
                case ResultReason.SynthesizingAudioCompleted:
                    speechBase64 = Convert.ToBase64String(speechResult.AudioData);
                    _logger.LogInformation("Speech synthesis completed");
                    break;
               
                default:
                    var errStr = $"Speech synthesis failed: {speechResult.Reason}";
                    throw new Exception(errStr);
            }

            // For successful result, return:
            //   new JsonResult(new { text = <AOAI Response>, speech = <Speech in Base64> })
            // For error, return: new JsonResult(new { error = <Error String> })
            return new JsonResult(new { text = aoaiResponse, speech = speechBase64 });
        }
        catch(Exception ex)
        {
            var errStr = $"{ex.Message}\n{ex.StackTrace}";
            _logger.LogError(errStr);
            return new JsonResult(new { error = errStr });
        }
    }
}
