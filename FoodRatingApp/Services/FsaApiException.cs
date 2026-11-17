namespace FoodRatingApp.Services;

/// <summary>
/// The FSA API answered, but with a response we could not use (e.g. invalid or empty JSON).
/// </summary>
public class FsaApiException : Exception
{
    public FsaApiException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
