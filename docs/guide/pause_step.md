# Add Pause and Step to a game

## Connect when gameplay is ready

Reference the BCL-only `Bed.Runtime` from the game. Load its saved data and prepare the simulation before connecting; keep one helper for that gameplay lifetime:

```csharp
using Bed.Runtime;

using var control = FrameControl.ConnectFromEnvironment();
```

BED waits for the initial hello as long as the owned child is alive, until Stop, exit or disposal. While a game is loading or at a menu without this connection, the controls say **This game hasn't enabled frame control yet.** Connect once the game can service simulation boundaries; do not advertise readiness and then stop polling in a menu.

## One permit, one complete tick

At each scheduled simulation boundary call `control.PollFrame()`. A true return permits exactly one complete simulation tick. A false return skips gameplay work. Continue polling at the next boundary even while paused: the next poll after a Step acknowledges that the permitted tick finished.

The [standalone route puzzle](../../samples/RoutePuzzle.Game/Program.cs) is a console example. It loads saved routes before connecting, then uses a 20 ms tick without an accumulator. Its loop is reproduced here; `game`, `choices`, `route` and the optional receipt are initialized in the linked program.

<!-- sample-loop:start -->
```csharp
while(watch.Elapsed.TotalSeconds<seconds)
{
    // Always pump the gate, even while paused. Each permit advances one 20ms tick; no catch-up accumulator.
    if(control.PollFrame())
    {
        ticks++;
        if(choices.TryDequeue(out var chosen))game.Choose(chosen);
        else if(route.Count>0&&ticks%10==0)game.Choose(route.Dequeue());
        receipt?.WriteLine($"{ticks}|{game.Current}|{game.Energy}");
    }
    if(game.Status!=previous){previous=game.Status;Console.WriteLine(previous);}
    Thread.Sleep(20);
}
```
<!-- sample-loop:end -->

Input may arrive while paused, but the sample applies route choices only inside the permitted branch. The game needs no editor dependency or stdout control protocol. Its status output remains ordinary game output.

## Keep rendering and discard paused time

A graphical game should pump window events and draw its current state on every render iteration, including those with no permitted simulation tick. Queue gameplay input and apply it only in the permitted update. Keep economy, physics, timers and any UI callbacks that mutate gameplay inside that boundary; a renderer or HUD callback must not advance them while paused.

Schedule polls from a monotonic clock. Extra render iterations before the next scheduled tick must not obtain empty permits. Discard paused wall time and reset the scheduling baseline after a denied poll. Do not grant one permit and then run a backlog of ticks. If you keep a bounded running catch-up budget, gate each individual tick and clear that budget when paused; the first resumed or disconnected poll must not replay the pause interval. The console sample's simple cadence drops elapsed time instead of catching up.

## Check the integration

In **Play console**, **Pause simulation (F9)** becomes effective after its acknowledgement. **Step one frame (F10)** requires acknowledged Paused and runs one simulation tick, regardless of how often you redraw. **Resume simulation (F11)** returns to normal progression. Only one command may be pending.

Test that paused renders leave game state and gameplay timers unchanged, each Step advances exactly once, the following poll acknowledges without a second tick, and Resume after a long pause creates no burst. Keep window close and BED's Stop responsive. Without a valid launch environment the helper permits ordinary simulation. Pipe loss releases the gate at the next poll; the connection is not retried for that run. After the handshake, a five-second acknowledgement timeout disables BED's controls and reports unknown state; it does not terminate the game. Dispose the helper when gameplay ends.

See [Play](playing.md) for saved-file launching and controls, and [Runtime](runtime.md) for reading prepared game data.
