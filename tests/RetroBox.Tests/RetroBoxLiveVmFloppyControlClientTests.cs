using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxLiveVmFloppyControlClientTests
{
    [Fact]
    public async Task Commands_go_to_86box_while_its_socket_answers()
    {
        var box86 = new EngineStub("86box");
        var qemu = new EngineStub("qemu") { Live = false };
        var client = new RetroBoxLiveVmFloppyControlClient(box86, qemu);

        await client.InsertAsync(0, "/data/a.img", readOnly: true);
        await client.EjectAsync(0);

        Assert.Equal(["insert:/data/a.img:True", "eject"], box86.Calls);
        Assert.Empty(qemu.Calls);
    }

    [Fact]
    public async Task Commands_fall_through_to_qemu_when_86box_is_unavailable_and_stay_there()
    {
        var box86 = new EngineStub("86box") { Live = false };
        var qemu = new EngineStub("qemu");
        var client = new RetroBoxLiveVmFloppyControlClient(box86, qemu);

        var status = await client.StatusAsync(0);
        await client.InsertAsync(0, "/data/a.img", readOnly: false);

        Assert.Equal("qemu", status.Path);
        Assert.Equal(["status", "insert:/data/a.img:False"], qemu.Calls);
        Assert.Equal(1, box86.Attempts);
    }

    [Fact]
    public async Task The_next_vm_is_found_after_a_switch()
    {
        var box86 = new EngineStub("86box") { Live = false };
        var qemu = new EngineStub("qemu");
        var client = new RetroBoxLiveVmFloppyControlClient(box86, qemu);
        await client.StatusAsync(0);

        qemu.Live = false;
        box86.Live = true;
        var status = await client.StatusAsync(0);

        Assert.Equal("86box", status.Path);
    }

    [Fact]
    public async Task A_command_the_live_vm_refuses_is_not_retried_on_the_other_engine()
    {
        var box86 = new EngineStub("86box") { Live = false };
        var qemu = new EngineStub("qemu")
        {
            Failure = new RetroBoxFloppyControlException("missing_image", "Could not open"),
        };
        var client = new RetroBoxLiveVmFloppyControlClient(box86, qemu);
        await Assert.ThrowsAsync<RetroBoxFloppyControlException>(() => client.StatusAsync(0));
        var box86Attempts = box86.Attempts;

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlException>(
            () => client.InsertAsync(0, "/data/gone.img", readOnly: true));

        Assert.Equal("missing_image", error.Code);
        Assert.Equal(box86Attempts, box86.Attempts);
    }

    [Fact]
    public async Task No_running_vm_is_unavailable_and_names_both_sockets()
    {
        var client = new RetroBoxLiveVmFloppyControlClient(
            new EngineStub("86box") { Live = false },
            new EngineStub("qemu") { Live = false });

        var error = await Assert.ThrowsAsync<RetroBoxFloppyControlUnavailableException>(() => client.StatusAsync(0));

        Assert.Equal("internal_failure", error.Code);
        Assert.Contains("86box socket is unavailable", error.Message, StringComparison.Ordinal);
        Assert.Contains("qemu socket is unavailable", error.Message, StringComparison.Ordinal);
    }

    private sealed class EngineStub(string name) : IRetroBoxFloppyControlClient
    {
        public bool Live { get; set; } = true;

        public RetroBoxFloppyControlException? Failure { get; init; }

        public List<string> Calls { get; } = [];

        public int Attempts { get; private set; }

        public Task<RetroBoxFloppyStatus> InsertAsync(int drive, string imagePath, bool readOnly, CancellationToken cancellationToken = default) =>
            Answer($"insert:{imagePath}:{readOnly}");

        public Task<RetroBoxFloppyStatus> EjectAsync(int drive, CancellationToken cancellationToken = default) =>
            Answer("eject");

        public Task<RetroBoxFloppyStatus> StatusAsync(int drive, CancellationToken cancellationToken = default) =>
            Answer("status");

        private Task<RetroBoxFloppyStatus> Answer(string call)
        {
            Attempts++;
            if (!Live)
            {
                throw new RetroBoxFloppyControlUnavailableException($"{name} socket is unavailable");
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            Calls.Add(call);
            return Task.FromResult(new RetroBoxFloppyStatus(0, true, name, false, false, false));
        }
    }
}
