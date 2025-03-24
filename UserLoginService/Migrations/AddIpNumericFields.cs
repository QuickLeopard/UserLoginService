using Microsoft.EntityFrameworkCore.Migrations;

namespace UserLoginService.Migrations
{
    public partial class AddIpNumericFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "ip_numeric_high",
                table: "user_login_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.AddColumn<ulong>(
                name: "ip_numeric_low",
                table: "user_login_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0ul);

            // Add an index for faster IP pattern matching
            migrationBuilder.CreateIndex(
                name: "IX_UserLoginRecord_IpNumeric",
                table: "user_login_records",
                columns: new[] { "ip_numeric_high", "ip_numeric_low" });
                
            // Update existing records to populate the numeric fields
            migrationBuilder.Sql(@"
                -- PostgreSQL function to convert IP to numeric for both IPv4 and IPv6
                CREATE OR REPLACE FUNCTION ip_to_numeric(ip_address text, OUT high_bits bigint, OUT low_bits bigint) AS $$
                DECLARE
                    ip_type text;
                    ip_bytes bytea;
                BEGIN
                    -- Check if it's IPv4 or IPv6
                    IF position('.' in ip_address) > 0 THEN
                        -- IPv4 processing
                        ip_type := 'inet';
                        high_bits := 0;
                        
                        -- Convert to integer representation
                        low_bits := (
                            SELECT split_part(ip_address, '.', 1)::bigint * 16777216 + 
                                   split_part(ip_address, '.', 2)::bigint * 65536 + 
                                   split_part(ip_address, '.', 3)::bigint * 256 + 
                                   split_part(ip_address, '.', 4)::bigint
                        );
                    ELSIF position(':' in ip_address) > 0 THEN
                        -- IPv6 processing (simplified)
                        -- This is a basic implementation and may need enhancement for complex IPv6 formats
                        ip_type := 'inet';
                        
                        -- Convert to bytea representation
                        ip_bytes := inet_to_bytea(ip_address::inet);
                        
                        -- Extract high and low parts
                        high_bits := 0;
                        low_bits := 0;
                        
                        -- First 8 bytes for high bits (simplified conversion)
                        FOR i IN 0..7 LOOP
                            high_bits := high_bits * 256 + get_byte(ip_bytes, i);
                        END LOOP;
                        
                        -- Last 8 bytes for low bits
                        FOR i IN 8..15 LOOP
                            low_bits := low_bits * 256 + get_byte(ip_bytes, i);
                        END LOOP;
                    ELSE
                        -- Unknown format
                        high_bits := 0;
                        low_bits := 0;
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                -- Update existing records
                UPDATE user_login_records
                SET 
                    ip_numeric_high = (ip_to_numeric(ip_address)).high_bits,
                    ip_numeric_low = (ip_to_numeric(ip_address)).low_bits;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserLoginRecord_IpNumeric",
                table: "user_login_records");
                
            migrationBuilder.DropColumn(
                name: "ip_numeric_high",
                table: "user_login_records");

            migrationBuilder.DropColumn(
                name: "ip_numeric_low",
                table: "user_login_records");
                
            // Drop the function if it exists
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS ip_to_numeric;");
        }
    }
}
