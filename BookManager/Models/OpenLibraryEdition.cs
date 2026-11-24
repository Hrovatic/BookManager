namespace BookManagerApi.Models
{
    public class OpenLibraryEdition
    {
        public string? Title { get; set; }
        public List<OpenLibraryKey>? Works { get; set; }
        public List<string>? Publishers { get; set; }
        public string? Publish_Date { get; set; }
        public List<int>? Covers { get; set; }
        public List<OpenLibraryKey>? Languages { get; set; }
        public List<string>? Subjects { get; set; }
    }

    public class OpenLibraryKey 
    { 
        public string? Key { get; set; }
    
    } 
    // Work lookup (Edition → Work)
    public class OpenLibraryWork 
    {
        public string? Title { get; set; } 
        public List<OpenLibraryAuthorRef>? Authors { get; set; } 
        public List<string>? Subjects { get; set; } 
        public List<OpenLibraryKey>? Languages { get; set; } 
    } 
    public class OpenLibraryAuthorRef
    { 
        public OpenLibraryKey? Author { get; set; } 
    } 
    
    // Author lookup
    public class OpenLibraryAuthor 
    { 
        public string? Name { get; set; }
    }
}
