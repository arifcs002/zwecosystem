using System;
using Npgsql;

class Program {
    static void Main() {
        var connStr = "Host=194.5.152.74;Port=5432;Database=ecommerce_db;Username=dev_user;Password=A3w%$Labu!!Mh#896;";
        using var conn = new NpgsqlConnection(connStr);
        conn.Open();
        
        using var cmd = new NpgsqlCommand("UPDATE users SET email = 'arifsuperadmin', first_name = 'Arif Super', last_name = 'Admin' WHERE email = 'arifowneradmin.bd'", conn);
        cmd.ExecuteNonQuery();
        Console.WriteLine("Updated email to arifsuperadmin");
    }
}
