namespace TodoApi.Dtos
{
    public record LoginDto(string Username, string Password);
    public record LoginResnonseDto(string Token, DateTime Expiration);

}