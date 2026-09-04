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

    [Fact]
    public void Parse_init_event()
    {
        var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent("INIT 1");

        var init = Assert.IsType<RetroBoxArduinoInitEvent>(serialEvent);
        Assert.Equal("1", init.Version);
    }

    [Theory]
    [InlineData("INIT")]
    [InlineData("INIT   ")]
    public void Parse_rejects_malformed_init_events(string line)
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
    public void Build_ping_command()
    {
        var command = RetroBoxArduinoSerialProtocol.BuildPingCommand();

        Assert.Equal("PING", command);
    }

    [Fact]
    public void Build_status_command()
    {
        var command = RetroBoxArduinoSerialProtocol.BuildStatusCommand();

        Assert.Equal("STATUS", command);
    }

    [Fact]
    public void Parse_pong_response()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("PONG");

        Assert.IsType<NfcResponse.Pong>(response);
    }

    [Fact]
    public void Parse_ok_response()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("OK");

        Assert.IsType<NfcResponse.Ok>(response);
    }

    [Theory]
    [InlineData("ERROR not written", "not written")]
    [InlineData("ERROR floppy missing", "floppy missing")]
    public void Parse_error_response(string line, string expectedMessage)
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse(line);

        var error = Assert.IsType<NfcResponse.Error>(response);
        Assert.Equal(expectedMessage, error.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("BOGUS", "BOGUS")]
    [InlineData("UNKNOWN_COMMAND", "UNKNOWN_COMMAND")]
    public void Parse_unknown_response(string? line, string? expectedLine)
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse(line);

        var unknown = Assert.IsType<NfcResponse.Unknown>(response);
        Assert.Equal(expectedLine, unknown.Line);
    }

    [Fact]
    public void Parse_strips_whitespace_before_matching()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("  PONG\r\n");

        Assert.IsType<NfcResponse.Pong>(response);
    }

    [Theory]
    [InlineData("bad id", "ro")]
    [InlineData("monkey1-disk1", "bad")]
    public void Build_write_command_rejects_invalid_payload(string id, string mode)
    {
        Assert.Throws<RetroBoxArduinoSerialProtocolException>(() =>
            RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode));
    }

    [Fact]
    public void BuildTagIdCommand_returns_the_firmware_verb()
    {
        Assert.Equal("TAGID", RetroBoxArduinoSerialProtocol.BuildTagIdCommand());
    }

    [Fact]
    public void ParseResponse_reads_a_tag_id()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("Tag ID: 04A13BFE");

        var tagId = Assert.IsType<NfcResponse.TagId>(response);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public void ParseResponse_trims_the_tag_id_line()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("  Tag ID: 04A13BFE\r\n");

        var tagId = Assert.IsType<NfcResponse.TagId>(response);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public void ParseResponse_ignores_a_tag_id_line_with_no_uid()
    {
        Assert.IsType<NfcResponse.Unknown>(RetroBoxArduinoSerialProtocol.ParseResponse("Tag ID: "));
    }

    [Fact]
    public void ParseResponse_keeps_no_tag_detected_as_an_error()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("ERROR no-tag-detected");

        var error = Assert.IsType<NfcResponse.Error>(response);
        Assert.Equal("no-tag-detected", error.Message);
    }
}
