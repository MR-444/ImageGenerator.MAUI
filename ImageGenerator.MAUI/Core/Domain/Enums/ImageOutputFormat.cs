using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Core.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageOutputFormat
{
    [EnumMember(Value = "jpeg")]
    Jpg,
    [EnumMember(Value = "png")]
    Png,
    [EnumMember(Value = "webp")] // if needed
    Webp
}

