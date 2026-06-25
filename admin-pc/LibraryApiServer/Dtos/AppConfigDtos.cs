namespace LibraryApiServer.Dtos;

public record AppConfigDto(string displayName, string updatedAt);

public record SetDisplayNameDto(string displayName);