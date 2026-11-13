using System.Security.Cryptography;

namespace DuckbillCalendar.Core;

/// <summary>One byte-level publication contract for publishers, offline checks and clients. No IO.</summary>
public static class FeedPublication
{
    public static FeedDocument ReadPayload(CurrentPointer pointer, ReadOnlySpan<byte> payload, bool allowExample = false)
    {
        var pointerErrors = FeedValidator.Validate(pointer, allowExample);
        if (pointerErrors.Count != 0) throw new InvalidDataException("ValidationFailed:" + pointerErrors[0]);
        if (payload.Length != pointer.Content.Bytes) throw new InvalidDataException("PayloadLengthMismatch");
        if (!Convert.ToHexStringLower(SHA256.HashData(payload)).Equals(pointer.Content.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PayloadHashMismatch");
        var feed = CalendarJson.Parse<FeedDocument>(payload);
        var errors = FeedValidator.Validate(feed, allowExample);
        if (errors.Count != 0) throw new InvalidDataException("ValidationFailed:" + errors[0]);
        if (feed.ContentRevision != pointer.Content.Revision || feed.SchemaVersion != pointer.Content.SchemaVersion ||
            feed.Region != pointer.Content.Region || feed.IsExample != pointer.IsExample) throw new InvalidDataException("PayloadReferenceMismatch");
        return feed;
    }
}
