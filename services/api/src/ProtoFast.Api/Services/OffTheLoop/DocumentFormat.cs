namespace ProtoFast.Api.Services.OffTheLoop;

/// <param name="Extension">Lower case, with its dot: ".pdf".</param>
public sealed record DocumentFormat(string Extension, string MediaType);
