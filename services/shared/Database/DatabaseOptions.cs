namespace ProtoFast.Database;

public class DatabaseOptions
{
    public required int CommandTimeout { get; set; } = 30;
    
    public DatabaseOptions() { } 
}