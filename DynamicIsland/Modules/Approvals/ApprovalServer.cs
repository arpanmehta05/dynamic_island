using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Modules.Approvals;

/// <summary>
/// Both ends of the link between Claude Code and the island. Claude Code runs <c>DynamicIsland.exe --claude-approve</c> (a PermissionRequest
/// hook) whenever it is about to ask you something; that little process (<see cref="RunHook"/>) passes the request to the running island
/// through a local pipe and prints your answer for Claude Code. If the island isn't running, it prints nothing and Claude Code asks as usual.
/// </summary>
sealed class ApprovalServer
{
    const string PipeName = "DynamicIsland.ClaudeApprovals";
    // after this long without an answer the hook steps aside and Claude Code shows its own question
    static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(75);

    public event Action<ApprovalRequest>? Requested;

    public ApprovalServer() => Task.Run(Listen);

    async Task Listen()
    {
        while (true)
        {
            try
            {
                var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                _ = Serve(pipe);
            }
            catch { await Task.Delay(1000); }
        }
    }

    async Task Serve(NamedPipeServerStream pipe)
    {
        try
        {
            using var _ = pipe;
            var reader = new StreamReader(pipe, Encoding.UTF8);
            var request = ApprovalRequest.Parse(await reader.ReadLineAsync() ?? "");
            if (request is null) return;

            Requested?.Invoke(request);
            var disconnected = reader.ReadLineAsync(); // completes (null) when the hook process ends, e.g. you answered in the terminal
            var finished = await Task.WhenAny(request.Answer, disconnected, Task.Delay(GiveUpAfter));
            if (finished != request.Answer) { request.Resolve("ask"); return; }

            await pipe.WriteAsync(Encoding.UTF8.GetBytes(request.Answer.Result + "\n"));
            await pipe.FlushAsync();
        }
        catch { /* the hook went away mid-answer */ }
    }

    /// <summary>The hook process: Claude Code's request arrives on stdin; the decision goes to stdout. Returns the process exit code.</summary>
    public static int RunHook()
    {
        try
        {
            var request = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8).ReadToEnd().Replace("\r", " ").Replace("\n", " ");
            var input = JsonNode.Parse(request)?.AsObject();
            var isPreToolUse = input?["hook_event_name"]?.GetValue<string>() == "PreToolUse";
            // questions are answered through the PreToolUse hook; if the permission hook also fires for one, stay out of the way
            if (!isPreToolUse && input?["tool_name"]?.GetValue<string>() == "AskUserQuestion") return 0;
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(400); // the island isn't running: say nothing, Claude Code asks you itself
            pipe.Write(Encoding.UTF8.GetBytes(request + "\n"));
            pipe.Flush();

            var answer = new StreamReader(pipe, Encoding.UTF8).ReadLine();
            string output;
            if (answer is not null && answer.StartsWith("answer:") && input?["tool_input"]?.DeepClone() is JsonObject updated)
            {
                // a question: hand Claude Code its own questions back with the answers filled in
                updated["answers"] = JsonNode.Parse(answer["answer:".Length..]);
                output = isPreToolUse
                    ? new JsonObject { ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PreToolUse", ["permissionDecision"] = "allow", ["updatedInput"] = updated } }.ToJsonString()
                    : new JsonObject { ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = new JsonObject { ["behavior"] = "allow", ["updatedInput"] = updated } } }.ToJsonString();
            }
            else if (answer is "allow" or "deny" && !isPreToolUse)
            {
                var decision = answer == "allow"
                    ? new { behavior = "allow" }
                    : (object)new { behavior = "deny", message = "Denied from Dynamic Island." };
                output = JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision } });
            }
            else return 0;
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(Encoding.UTF8.GetBytes(output));
        }
        catch (Exception ex) { App.Log("approval hook", ex); } // never get in the way of Claude Code: no output = Claude asks itself
        return 0;
    }
}
