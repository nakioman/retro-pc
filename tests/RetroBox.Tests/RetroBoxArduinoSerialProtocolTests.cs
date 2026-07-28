using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxArduinoSerialProtocolTests
{
    [Theory]
    [InlineData("INSERT monkey1-disk1,ro", "ro")]
    [InlineData("INSERT monkey1-disk1,rw", "rw")]
    [InlineData("INSERT monkey1-disk1", "ro")]
    public void Parse_insert_event(string line, string expectedMode)
    {
        var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent(line);

        var insert = Assert.IsType<RetroBoxArduinoInsertEvent>(serialEvent);
        Assert.Equal("monkey1-disk1", insert.Id);
        Assert.Equal(expectedMode, insert.Mode);
    }

    [Fact]
    public void Parse_eject()
    {
        var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent("EJECT");

        Assert.IsType<RetroBoxArduinoEjectEvent>(serialEvent);
    }

    [Fact]
    public void Parse_error_message()
    {
        var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent("ERROR unreadable");

        var error = Assert.IsType<RetroBoxArduinoErrorEvent>(serialEvent);
        Assert.Equal("unreadable", error.Message);
    }

    [Fact]
    public void Parse_trims_surrounding_serial_line_whitespace()
    {
        var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent(" \r\nINSERT monkey1-disk1,ro\r\n ");

        var insert = Assert.IsType<RetroBoxArduinoInsertEvent>(serialEvent);
        Assert.Equal("monkey1-disk1", insert.Id);
        Assert.Equal("ro", insert.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INSERT")]
    [InlineData("INSERT bad id,ro")]
    [InlineData("INSERT monkey1-disk1,bad")]
    [InlineData("INSERT monkey1-disk1,ro,extra")]
    [InlineData("ERROR")]
    [InlineData("READ")]
    [InlineData("insert monkey1-disk1,ro")]
    public void Parse_rejects_malformed_events(string line)
    {
        Assert.Throws<RetroBoxArduinoSerialProtocolException>(() =>
            RetroBoxArduinoSerialProtocol.ParseEvent(line));
    }

    [Theory]
    [InlineData("monkey1-disk1", "ro", "WRITE monkey1-disk1,ro")]
    [InlineData("monkey1-disk1", "rw", "WRITE monkey1-disk1,rw")]
    public void Build_write_command(string id, string mode, string expectedCommand)
    {
        var command = RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode);

        Assert.Equal(expectedCommand, command);
    }

    [Fact]
    public void Build_read_command()
    {
        var command = RetroBoxArduinoSerialProtocol.BuildReadCommand();

        Assert.Equal("READ", command);
    }

    [Theory]
    [InlineData("bad id", "ro")]
    [InlineData("monkey1-disk1", "bad")]
    public void Build_write_command_rejects_invalid_payload(string id, string mode)
    {
        Assert.Throws<RetroBoxArduinoSerialProtocolException>(() =>
            RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode));
    }
}
