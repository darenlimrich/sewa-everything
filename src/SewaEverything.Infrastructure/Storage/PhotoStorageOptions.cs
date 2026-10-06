using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Infrastructure.Storage;

public sealed class PhotoStorageOptions
{
    public const string SectionName = "Storage:Photos";

    public const string DiskProvider = "disk";

    public const string DatabaseProvider = "database";

    [RegularExpression("^(disk|database)$",
        ErrorMessage = "Storage:Photos:Provider hanya 'disk' atau 'database'.")]
    public string Provider { get; set; } = DiskProvider;

    public bool UsesDatabase => Provider == DatabaseProvider;

    [Required(ErrorMessage = "Storage:Photos:RootPath wajib diisi.")]
    public string RootPath { get; set; } = "storage/photos";

    [Required]
    [RegularExpression("^/[A-Za-z0-9._~/-]*[A-Za-z0-9._~-]$",
        ErrorMessage = "Storage:Photos:RequestPath harus diawali '/' dan tanpa '/' di akhir.")]
    public string RequestPath { get; set; } = "/uploads";

    [Range(1024, 50 * 1024 * 1024, ErrorMessage = "Batas ukuran foto harus 1 KB–50 MB.")]
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;

    [Range(1, 50)]
    public int MaxPhotosPerItem { get; set; } = 10;
}
