using Newtonsoft.Json;

namespace lucidRESUME.Collabora.Models;

public class WopiCheckFileInfo
{
    [JsonProperty("BaseFileName")]
    public string BaseFileName { get; set; } = string.Empty;
    
    [JsonProperty("OwnerId")]
    public string OwnerId { get; set; } = string.Empty;
    
    [JsonProperty("UserId")]
    public string UserId { get; set; } = string.Empty;
    
    [JsonProperty("UserFriendlyName")]
    public string UserFriendlyName { get; set; } = "User";
    
    [JsonProperty("Size")]
    public long Size { get; set; }
    
    [JsonProperty("Version")]
    public string Version { get; set; } = "1";
    
    [JsonProperty("FileExtension")]
    public string FileExtension { get; set; } = string.Empty;
    
    [JsonProperty("LastModifiedTime")]
    public string LastModifiedTime { get; set; } = DateTime.UtcNow.ToString("o");
    
    [JsonProperty("ReadOnly")]
    public bool ReadOnly { get; set; }
    
    [JsonProperty("UserCanWrite")]
    public bool UserCanWrite { get; set; } = true;
    
    [JsonProperty("SupportsUpdate")]
    public bool SupportsUpdate { get; set; } = true;
    
    [JsonProperty("SupportsLocks")]
    public bool SupportsLocks { get; set; } = true;
    
    [JsonProperty("SupportsGetLock")]
    public bool SupportsGetLock { get; set; } = true;
    
    [JsonProperty("LicenseCheckForWopi")]
    public bool LicenseCheckForWopi { get; set; } = false;
}

public class WopiLock
{
    public string LockId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
