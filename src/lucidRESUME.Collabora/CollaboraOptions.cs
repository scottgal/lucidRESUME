namespace lucidRESUME.Collabora;

public class CollaboraOptions
{
    public bool Enabled { get; set; }
    public string CodeUrl { get; set; } = "http://localhost:9980";
    public int WopiPort { get; set; } = 9981;
    public string HostUrl { get; set; } = "http://localhost:9981";
}
