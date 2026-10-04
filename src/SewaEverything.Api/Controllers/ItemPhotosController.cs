using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;
using SewaEverything.Infrastructure.Storage;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("items/{id:guid}/photos")]
[Authorize(Roles = Roles.Seller)]
public sealed class ItemPhotosController(
    SewaDbContext db,
    IPhotoStorage storage,
    IOptions<PhotoStorageOptions> options) : ItemScopedController(db)
{
    private const long AbsoluteBodyLimitBytes = 16 * 1024 * 1024;

    private readonly PhotoStorageOptions _options = options.Value;

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = AbsoluteBodyLimitBytes)]
    [ProducesResponseType<ItemPhotoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemPhotoResponse>> Upload(
        Guid id, [FromForm] IFormFile? file, [FromForm] int? sortOrder, CancellationToken ct)
    {
        var (item, error) = await LoadOwnedItemAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "Berkas foto wajib disertakan.");
            return ValidationProblem(ModelState);
        }

        if (file.Length > _options.MaxBytes)
        {
            ModelState.AddModelError(nameof(file),
                $"Ukuran foto maksimal {_options.MaxBytes / (1024 * 1024)} MB.");
            return ValidationProblem(ModelState);
        }

        var existing = await Db.ItemPhotos.CountAsync(p => p.ItemId == item!.Id, ct);

        if (existing >= _options.MaxPhotosPerItem)
        {
            return Problem(
                title: "Foto sudah penuh",
                detail: $"Satu barang maksimal {_options.MaxPhotosPerItem} foto. " +
                        "Hapus salah satu sebelum menambah yang baru.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await using var content = file.OpenReadStream();

        var header = new byte[ImageSniffer.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

        if (!ImageSniffer.TryDetect(header.AsSpan(0, read), out var format))
        {
            ModelState.AddModelError(nameof(file),
                "Berkas ini bukan gambar JPEG, PNG, atau WebP.");
            return ValidationProblem(ModelState);
        }

        content.Position = 0;

        using var mentah = new MemoryStream();
        await content.CopyToAsync(mentah, ct);

        var bersih = ImageSanitizer.Sanitize(mentah.GetBuffer().AsSpan(0, (int)mentah.Length), format);

        if (!bersih.Ok)
        {
            ModelState.AddModelError(nameof(file), bersih.Error!);
            return ValidationProblem(ModelState);
        }

        using var siap = new MemoryStream(bersih.Bytes!, writable: false);

        var order = sortOrder
            ?? (await Db.ItemPhotos
                    .Where(p => p.ItemId == item!.Id)
                    .MaxAsync(p => (int?)p.SortOrder, ct) ?? -1) + 1;

        var url = await storage.SaveAsync(siap, format, ct);

        var photo = new ItemPhoto
        {
            ItemId    = item!.Id,
            Url       = url,
            SortOrder = order
        };

        Db.ItemPhotos.Add(photo);
        item!.ContentChanged();

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(url, CancellationToken.None);
            throw;
        }

        return Created(url, photo.ToResponse());
    }

    [HttpDelete("{photoId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, Guid photoId, CancellationToken ct)
    {
        var (item, error) = await LoadOwnedItemAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var photo = await Db.ItemPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ItemId == item!.Id, ct);

        if (photo is null)
        {
            return Problem(
                title: "Foto tidak ditemukan",
                detail: $"Barang ini tidak punya foto dengan id {photoId}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        Db.ItemPhotos.Remove(photo);
        item!.Revised();
        await Db.SaveChangesAsync(ct);

        await storage.DeleteAsync(photo.Url, ct);

        return NoContent();
    }
}
