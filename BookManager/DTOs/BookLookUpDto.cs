namespace BookManagerApi.DTOs
{
    //Ta DTO ti da samo edition, ki ima shranjeno zelo malo podatkov in se potem uporabi OpenLibraryEdition.cs za pridobivanje bolj podrobnij
    public class BookLookUpDto
    {
        public string? Title { get; set; }
        public List<AuthorDto>? Authors { get; set; }
        public List<PublisherDto>? Publishers { get; set; }
        public string? Publish_Date { get; set; }
        public List<LanguageDto>? Languages { get; set; }
    }

    public class AuthorDto { public string? Name { get; set; } }
    public class PublisherDto { public string? Name { get; set; } }
    public class LanguageDto { public string? Key { get; set; } } 
    
    // OpenLibrary vrne dictionary: "ISBN:xxxx": { ... }
    public class OpenLibraryDataResponse : Dictionary<string, BookLookUpDto> {}
}
