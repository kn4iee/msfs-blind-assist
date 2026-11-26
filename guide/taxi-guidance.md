---
title: "Taxi Guidance"
---

<div class="guide-part" markdown="1">

Chapter 2 of 4 in [Part 5: On the Ground](index.md#part-4-on-the-ground) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [How Taxi Guidance Works](#taxi-overview)
- [Before You Start](#taxi-before-you-start)
- [The Taxi Planner](#taxi-planner)
- [Choosing Your Destination](#taxi-destination-types)
- [Entering an ATC Clearance](#taxi-clearances)
- [Worked Examples](#taxi-examples)
- [Changing a Clearance on the Move](#taxi-amend)
- [SayIntentions Taxi Import](#taxi-sayintentions)
- [Progressive Taxi](#taxi-progressive)
- [Calculating the Route](#taxi-calculating)
- [Starting Guidance](#taxi-starting)
- [The Steering Tone](#taxi-tone)
- [Following the Guidance](#taxi-following)
- [Hold Shorts and Continue](#taxi-holds)
- [Runway Crossings](#taxi-crossings)
- [Lining Up on the Runway](#taxi-lineup)
- [Going Off Route and Recalculation](#taxi-off-route)
- [Arriving at a Stand](#taxi-gate-arrival)
- [Taxi Routes in the Route Briefing](#taxi-briefing)
- [Hotkeys During Taxi](#taxi-hotkeys)
- [Taxi Guidance Settings](#taxi-settings)
- [Everything Taxi Guidance Says](#taxi-callouts)
- [If Something Goes Wrong](#taxi-troubleshooting)

</nav>

Taxi guidance is how MSFS Blind Assist gets you from a stand to a runway, from a runway exit to a stand, or anywhere else on an airport, following the route ATC actually gave you. It is the feature most pilots use on every flight.

## How Taxi Guidance Works {#taxi-overview}

Taxi guidance gives you two things at once.

- **A steering tone** for the fine steering, second by second: tone in your left ear, steer left; in your right ear, steer right; quiet, you are pointing the right way.
- **Spoken instructions** for the planning: turns coming up, the taxiway you have joined, runways you must stop before, the distance to your stand, and when you are lined up.

Follow the tone, and listen for the words.

Here is how the pieces fit together on a typical departure:

1. You open the **taxi planner** (Input Shift+Y). Its window is titled "Taxi Guidance". You choose where you are going, and enter the clearance ATC gave you as a list of steps, in the order ATC said them: the taxiways, any runway you are cleared to cross, and any hold short.
2. You press **Calculate Route**. BA works out the route over your scenery's real taxiways, adds a stop before every runway the route crosses, and tells you what it has built.
3. Guidance starts straight away. You release the parking brake, taxi, and follow the tone and the callouts.
4. At each runway hold line BA tells you to stop, unless you entered the crossing ATC cleared you for. When ATC clears you across or onto the runway, you press **Continue** (Input Y).
5. At the departure runway, BA lines you up on the centreline, tells you when you are lined up, and can hand you over to Takeoff Assist automatically.

After landing it works the same way in reverse: you plan a route from the runway exit to your stand, and BA takes you there and hands you to docking guidance at the gate.

Taxi guidance works at any airport your navigation database covers, and every name in its lists is what that airport's scenery calls things.

## Before You Start {#taxi-before-you-start}

Taxi guidance needs:

- **A [navigation database](getting-started.md#nav-database).** Without one, the planner says *"Airport database not available. Configure database in settings."*
- **A connection to the simulator,** with your aircraft on the ground at the airport. BA starts the route from wherever you are standing.

A few things make the experience better.

- **Online taxiway names** (on by default) fill in names your scenery leaves blank and add the names of painted holding points such as "A2", which the named holding point features need. See [Online names](knowing-where-you-are.md#where-names-online).
- **Rebuilding your database after a scenery change**, or BA will still be using the old airport's taxiways.
- **Knowing where you are.** Output Alt+Y ([Where Am I](knowing-where-you-are.md#where-am-i)) is a good first check before you plan a route.

## The Taxi Planner {#taxi-planner}

Open the taxi planner with Input Shift+Y. The window is titled **Taxi Guidance**, and its controls run in the order a controller reads a clearance: where you are, where you are going, and then the clearance itself, one step at a time, in the words ATC used. Some controls appear only when they apply (stand filters for a stand, runway options for a runway, the At box for a runway crossing), so the Tab order changes with what you choose.

Where focus lands when the window opens:

- The first time, on **Airport (ICAO)**.
- After that, while no route is active, on **Destination type**, so you always hear whether a destination is set.
- While a route is active, on **Step kind**, ready for the next instruction.

Pressing Escape closes the window, except that in an open drop-down list the first Escape only closes the list, and while you are changing a step it keeps the step as it was (see [Changing a step](#taxi-change-step)). **Closing the window does not stop guidance**; use the **Stop Guidance** button or Input Ctrl+Y for that. While a route is active, the **Clearance** list shows only what is still ahead: when you open the planner again, the steps you have already completed are gone, so amending a clearance mid-taxi is quick (see [Changing a Clearance on the Move](#taxi-amend)). The list is emptied when the leg it was calculated for is over: when you arrive, when a Progressive Taxi leg ends, when you stop guidance, and when you land. It is also emptied when you change airport.

Here is every control, in Tab order.

### Airport (ICAO) {#airport-icao}

The airport's identifier, filled in for you with the airport you are standing at. Type a different one and Tab away to load another airport. The status line under the Calculate button shows progress while it loads, for example *"EGLL: fetching taxiway names…"*, then *"EGLL: building taxi graph…"*, then a line such as *"EGLL: 6085 nodes, 9120 paths."* when the airport is ready. If the airport is missing you will see *"Airport XXXX not found in database."*, and if the scenery has no taxiway data at all, *"No taxi data for XXXX."*

### Destination type {#destination-type}

A list with five choices:

- **Runway** — taxi to a runway for departure.
- **Gate, stand or parking** — taxi to a stand, gate or parking spot.
- **Progressive Taxi (no destination)** — taxi one leg at a time, with no final destination yet: the leg ends where your steps say, for example at a hold short.
- **De-icing area** — taxi to a remote de-icing pad (these come from GSX airport profiles).
- **Place** — taxi to a named place such as an FBO, hangar, fuel island, terminal or cargo area.

Each one is explained in [Choosing Your Destination](#taxi-destination-types).

### Gate search {#gate-search}

Only shown for Gate, stand or parking. Type a gate letter or number, such as "B12" or just "12", and the destination list shrinks to the stands that match. It also matches the names stands are known by online, so if ATC calls a stand by its real-world name and your scenery spells it differently, typing the real-world name will usually still find it.

### Destination {#destination}

The list of places you can go, which depends on the destination type.

- For runways, each item reads like *"Runway 27L"*. Closed runways are left out. A runway whose full length can only be reached by taxiing along it first reads *"Runway 28 (backtrack required)"*; see [Full-length departure (requires backtrack)](#full-length-departure-requires-backtrack).
- For stands, each item names the stand, its type and its equipment, for example *"Gate A 25 - Gate Medium (Jetway)"*. The stands nearest to you by taxi distance come first, so a stand just across a runway, close as it looks, is listed behind the ones you can reach without going all the way round. If the stand is also known by another name online, the item ends with something like *", also 25 (online)"*. A stand the taxiway network does not reach is still listed, with *"(no taxi route)"* on the end, so you know it exists; BA will tell you it cannot route there if you choose it.
- For places, each item names the place, its kind and the stand it ends at.

When you pick a runway and BA has online data for that airport's painted holding points, it may also tell you which painted line the normal full-length departure uses, for example *"Full length holding point A1."* Without that data it names the taxiway the full-length departure enters by, for example *"Full length entry via N9."* Either helps you check the default matches your clearance before you calculate.

### Runway entry {#intersection-departure}

Runway only. A list (Alt+I) that says where you enter the runway. It starts on **Full length**, the normal departure from the start of the runway, and choosing a runway, or another destination type, puts it back there. After Full length come every taxiway intersection and every painted holding point of that runway, in order down the runway from its start, each with how much runway you will have ahead of you: *"B3, 2400 metres remaining"*, *"A1 (holding point), 3600 metres remaining"*. Distances are in metres or feet, depending on your setting. Choose the one ATC gave you; when ATC names no entry, leave it on Full length.

An intersection is a taxiway that meets the runway partway down. Choose one when ATC clears you to depart from there rather than from the full length, as in "line up runway 27L at B3". The taxiway of the normal full-length entrance is not listed as an intersection: that entrance is Full length. With nothing else to offer, the list holds Full length alone.

The list is brought up to date each time you reach it or open it, and keeps your choice, because what it offers can change after you choose: the distances are measured from your side of the runway, and the holding point names come from online data that can arrive later. If the entry you chose is no longer offered, the list goes back to Full length and you hear it once, for example *"B3 is no longer offered. Full length departure."*

When you calculate, BA confirms your entry, for example *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."* See [Runway, intersection or named holding point](#runway-intersection-or-named-holding-point).

### Holding points in the Runway entry list {#depart-from-named-holding-point}

Controllers often name the painted holding point you should use, such as "hold short A2", even when the taxiway in your scenery is not called A2. The Runway entry list offers these painted lines by name, among the intersections, with *"(holding point)"* after the name: *"A2 (holding point), 3600 metres remaining"*. A line set further back to protect the ILS says so, *"A3 (holding point, ILS hold), 3600 metres remaining"*, and so does a line out on the taxiway system, *"K2 (holding point, intermediate hold), 2900 metres remaining"*, so two lines at one entrance can be told apart. The holding points at the full-length end are listed too, right after Full length, because choosing between them ("A1" or "A2") is often exactly what ATC means.

Choose one and BA routes you through that exact painted line, not just to the runway entrance it shares with its neighbours. These names come from online data, so they need the online names setting switched on and the airport's data loaded; without them the list offers Full length and the intersections only.

### CAT 3 / low-visibility hold (LVP) {#cat-iii-low-visibility-hold-lvp}

Runway only. A checkbox (Alt+3); your screen reader calls it "CAT three, low visibility hold". By default BA holds you at the line closest to the runway, which is what a normal clearance means. When low-visibility procedures are in force, ATC holds you further back at the CAT II/III line, to keep you clear of the ILS critical area. Tick this box when ATC says so, or when you hear "low visibility procedures in force" on the ATIS.

If BA knows the name of that line from online data, choosing a runway with this box ticked tells you, for example *"CAT three hold A3."*, or *"CAT three hold not named in online data. Full length holding point A1."* Ticking the box tells you the same while the Runway entry list is on Full length; with another entry chosen, BA says nothing, because that entry is where you are going. After you calculate, the route summary tells you what it actually found: *"Holding at the CAT three ILS hold."*, *"No CAT three ILS hold found at this holding point. Holding at the full-length line."*, or *"No CAT three ILS hold found before the runway."*

### Full-length departure (requires backtrack) {#full-length-departure-requires-backtrack}

Runway only. A checkbox. Some runways have no taxiway reaching their very end. To use the full length you enter partway down, taxi along the runway to the end (a backtrack, or "backtaxi"), turn around, and line up. Tick this box when you need that. It is off by default, and nothing about backtracking happens unless you tick it. A backtrack takes priority over an entry chosen in the Runway entry list, so leave that list on Full length. See [Backtracking for a full-length departure](#taxi-backtrack-departure).

### Show fitting only and Hide occupied stands {#show-fitting-only-and-hide-occupied-stands}

Gate, stand or parking and Place only. Two checkboxes that hide stands too small for your aircraft and stands already occupied. Untick them if a stand you need is missing. See [Choosing a Stand](gates-gsx-and-docking.md#gsx-choosing-a-stand).

### Step kind and name {#first-taxiway}

You enter the clearance one step at a time, in the order ATC said it. Each step has a kind and a name.

**Step kind** (Alt+K) is a list of five kinds, each starting with its own letter:

- **Via taxiway** — a taxiway to follow: "taxi via A", "continue on K", "join B".
- **Left onto taxiway** and **Right onto taxiway** — a taxiway ATC told you to turn onto in a named direction: "left onto P". See [Left onto and Right onto](#taxi-turn-list).
- **Cross** — a runway ATC cleared you to cross, or a taxiway ATC said you cross. See [Cross and the At box](#taxi-step-cross).
- **Hold short of** — a runway, a taxiway or a named holding point to stop before. See [Hold short of](#hold-short-of-runway-after-the-first-taxiway).

You rarely need the Step kind list itself: **Alt+V**, **Alt+L**, **Alt+R**, **Alt+X** and **Alt+H** each choose a kind (Via, Left onto, Right onto, Cross, Hold short of) and move you straight to the name box, from anywhere in the window.

The **name box** (Alt+W) comes next. Its name follows the kind, and your screen reader says it: "Taxiway" for Via, Left onto and Right onto, "Runway or taxiway to cross" for Cross, and "Hold short of runway, taxiway or holding point" for Hold short of. Press the first letter or digit of a name to jump to it, then Enter to add the step (see [Add Step](#add-taxiway)).

For a taxiway, the list is ordered to put the likely names first:

- While the Clearance list has no taxiway yet, every taxiway at the airport is listed, nearest first in the direction you are facing.
- After a Via, Left onto or Right onto step, the taxiways that taxiway meets come first, nearest to you first, then every other taxiway at the airport in alphabetical order.
- The taxiway of the step just above is left out, because choosing the same taxiway twice in a row usually means a slip of the finger. After a step of any other kind it is listed again, which is how clearances such as "N, hold short of runway 15R, N" are entered (see [Worked examples](#taxi-examples)).

Nothing is chosen in the name box until you choose it, so Enter never adds a name you did not pick. Press Enter with nothing chosen and you hear *"Choose a name first."* The one exception is the very first step:

<div class="callout warning" role="note" markdown="1">

**Warning:** While the Clearance list is empty, BA preselects the nearest taxiway in the direction you are facing, as a guess. Always check it and choose the first taxiway ATC actually said before you press Enter. If ATC gave you no taxiways, or you just want BA to find the way to your destination, enter no steps at all.

</div>

### Left onto and Right onto {#taxi-turn-list}

Choose **Left onto taxiway** (Alt+L) or **Right onto taxiway** (Alt+R) instead of Via only when ATC names the direction of a turn, as in "taxi via F, H, left on C": enter Via F, Via H, then Left onto C. With Via, BA picks the turn that suits your route, as it always has.

On the first step, left and right are judged from the way your aircraft is facing when you press Calculate Route. The exception is a route that first follows another named taxiway to reach it, or more than 200 metres (about 650 feet) of unnamed taxi lines: then they are judged from the direction you arrive from. At a nose-in stand, calculate after pushback, when you are facing the way you will taxi, or enter the first taxiway as Via; calculated before pushback, the turn would be judged from a heading that points at the terminal. When your position is not connected to the taxiway network, a turn onto the first taxiway is made from where the route starts, never from another point, so the unmapped stretch BA warned you about stays the one you taxi across. On every later step, left and right are from the direction of the taxiway before it.

A Left onto or Right onto step is part of the clearance like any other step: it goes when the list is emptied after the leg, so it never turns the next route, and the SayIntentions fill never enters one. Once you have made the turn and are on that taxiway, the planner shows the step as Via, so calculating again never judges the turn a second time.

When BA cannot make the turn you entered, it tells you and keeps its own route. See [Warnings in the summary](#warnings-in-the-summary-and-what-to-do), and [Off-chart legs](#taxi-off-chart-legs) for the case where the airport's taxi data has no link that way.

### Cross and the At box {#taxi-step-cross}

Choose **Cross** (Alt+X) when ATC clears you across a runway: "cross runway 24L". The name box lists the runways first, those the taxiway of your last Via step crosses at the top, then the taxiways. Press the runway's first digit to jump to it. The step reads *"Cross runway 24L"*.

A Cross runway step clears that one crossing. BA does not stop you at that runway's hold line, but still counts you down to it and names it as you cross (see [Crossings you are cleared for](#cleared-crossings)). Every other runway, and any other crossing of the same runway, keeps its stop. If ATC takes the crossing back, press Input Alt+Y to stop at the hold line after all (see [Holding short after all](#holding-short-after-all)). A runway entered both as a Cross step and as a Hold short of step is refused when you calculate, naming both steps by their number in the Clearance list: *"Steps 2 and 4 both name runway 24L, one as a crossing and one as a hold short. Remove one."*

When the name is a runway, an **At (optional)** box appears just after the name box; press Tab to reach it. It starts on **(none)**, then lists the taxiways that cross that runway, then the painted holding points from which you can cross it on your side, each marked "(holding point)". Choose one when ATC says where: "cross runway 24L at P" reads *"Cross runway 24L at P"*.

- With **(none)**, the crossing cleared is the first place your route crosses that runway after the taxiway the step follows.
- With a taxiway or holding point, only the crossing there is cleared. If your route crosses the runway only somewhere else, that crossing keeps its stop, and Calculate says so (see [Warnings in the summary](#warnings-in-the-summary-and-what-to-do)).
- The At never bends the route, except at the end of a Progressive Taxi leg that ends across the runway: there the leg crosses at the taxiway or holding point you chose.

Choose a taxiway for "cross Bravo". It is a check: BA tells you *"The route does not cross taxiway B."* when your route never meets it, and otherwise says nothing. It never changes the route, with one exception at the end of a Progressive Taxi leg: "via A, cross B, hold short of C", entered as Via A, Cross taxiway B, Hold short of taxiway C, holds short of the C beyond B where A meets C more than once. A Progressive leg cannot end on a Cross taxiway step.

### Hold short of {#hold-short-of-runway-after-the-first-taxiway}

Choose **Hold short of** (Alt+H) when ATC tells you to stop before something: "hold short of runway 04R", "hold short of taxiway L", "hold short of L2", or "hold at B2". The name box lists every runway first, those the taxiway of your last Via step crosses at the top, then every taxiway and every named holding point at the airport, together in name order. A taxiway is listed by its name alone, and a holding point has "(holding point)" after its name: "L", "L1 (holding point)", "L2 (holding point)", "M". Press the first letter of a name to jump to it. The step reads *"Hold short of runway 04R"*, *"Hold short of taxiway L"* or *"Hold short of L2 (holding point)"*.

A Hold short of step is a stop: guidance stops you there and waits for you to press Continue (Input Y). The one exception is the step that ends a Progressive Taxi leg, which is where the leg ends (see [Progressive Taxi](#taxi-progressive)). You can enter as many hold shorts after one taxiway as ATC gives you.

Where a step applies is decided by your route, not by its place in the list. BA looks for the runway, taxiway or holding point from the taxiway of the Via step before it, and if your route does not reach it after that, anywhere along the route. So a hold short a controller gives at the end, although it lies in the middle of the route, can simply be entered last.

- **A runway.** BA already stops you before every runway your route crosses or enters, so this is optional, but it makes sure the stop is exactly the one ATC named, labelled the way you chose it. "Hold short of" your own departure runway, as the built-in MSFS ATC says it, is accepted and adds nothing: BA already holds you there.
- **A taxiway or a holding point.** See the next section.

In Progressive Taxi, where the online data has no name for a line ATC uses, the list can also offer one from the scenery's own signs, marked *"(holding point, from sign, approximate)"*: BA stops you at the sign's position, which may not be exactly on a painted line. Such a point can only end the leg. Entered anywhere else, Calculate says, for example, *"KK is read from a sign and can only end a leg."*

### Holding short of a taxiway or a holding point {#hold-short-after-the-first-taxiway}

- Choose a taxiway for "via A, hold short of taxiway B". Guidance stops before the place where your route first meets that taxiway, with *"Stop. Hold short of taxiway B. Press continue when cleared."* Where the route leaves room, the stop is far enough back for your whole aircraft to stay clear of taxiway B. When B is closer than that to your turn onto the taxiway of the Via step before it, BA stops you before the turn, on the taxiway before it, whether or not that step is Left onto or Right onto; you make the turn after you press Continue. Where a runway you cross lies just before B, BA stops you close to B: just past the runway, or where your route meets B. The nose of a large aircraft can then be on taxiway B, not just near its edge, and so it can when the taxiway you stop on carries on and meets B just past the turn.
- Choose a holding point for "via L, hold short of L2". Guidance stops at that point, with *"Stop. Hold short of holding point L2. Press continue when cleared."* The holding points come from online data, so they need the online names setting switched on and the airport's data loaded.

If your route never reaches the taxiway or holding point you chose, the route still loads and the summary ends with a note such as *"Note: requested hold-short(s) not on route — taxiway B (the route does not meet it)."* or *"Note: requested hold-short(s) not on route — holding point L2 (the route does not pass it)."*

If the first place your route meets the taxiway you chose is on a runway, right beside one, or so close past one that the stop would be called while you are still on or beside it, the note is *"Note: no place to hold short of taxiway B clear of the runway; no stop will be called there."* BA places no stop for that taxiway, not even where the route meets it again further on. A runway the route crosses still gets its own stop before it, but watch for that taxiway yourself.

Two more notes tell you a holding point you chose gets no stop of its own:

- *"Note: this route starts at holding point L2. No stop will be called there."* The route begins at the point itself, so you are at it or just short of it, and there is nothing left to count down. You can also hear it after *"Route changed"*, when a new route is worked out close to the point. Stop there yourself until you are cleared.
- *"Note: holding point A1 lies beyond the Runway 16 hold short point, so no stop is called there."* The point is past the line where guidance already stops you before your runway.

When the holding point you chose is your runway's own hold line, the countdown names both: *"Hold short Runway 16 at holding point A1 in 100 metres."*

### Add Step {#add-taxiway}

**Add Step** (Alt+D) adds the step to the end of the Clearance list. Enter in the Step kind list, the name box or the At box does the same. BA reads the step back once, numbered, for example *"Step 2: cross runway 24L at P."* Focus then stays in, or comes back to, the name box, with the kind back on Via taxiway and no name chosen, so the next taxiway costs a letter and Enter.

While you are changing a step, the button reads **Replace step 3** (still Alt+D) and replaces that step instead of adding one; see [Changing a step](#taxi-change-step).

### Clearance {#taxi-clearance-list}

The **Clearance** list (Alt+U) holds the steps you have entered, in order, each read as a sentence: *"Left onto P"*, *"Cross runway 24L at P"*, *"Hold short of taxiway L"*. Arrow through it to hear the clearance back.

Enter changes the selected step in place; see [Changing a step](#taxi-change-step).

Delete removes the selected step. While steps remain BA says nothing, because your screen reader reads the step now in its place; when you delete the last one you hear *"Steps cleared."*

### Changing a step {#taxi-change-step}

To correct a step, or to follow ATC when they change one ("continue via D instead of C"), select it in the Clearance list and press Enter. Its kind and name, and for a runway crossing its At, are put into the step boxes, and focus goes to the name box, whose name now ends with "changing step 3": that is how your screen reader tells you which step you are changing, and BA says nothing more. Add Step now reads **Replace step 3**.

Change what you need, the kind, the name or the At, and press Enter, or Alt+D. BA puts the new step in place of the old one and reads it back once, with its number: *"Step 3: via D."* Focus is then in the name box with the kind back on Via taxiway, as after Add Step, ready for a new step at the end of the list.

The name box lists the names for that place in the clearance, as if the steps above it were the whole list: after a taxiway, the taxiways it meets come first, and the taxiway of the step just above is left out, as usual. If the step's own name is no longer offered, for example a holding point BA no longer has, nothing is chosen in the name box, and Enter says *"Choose a name first."* until you choose one. A runway crossing keeps its own place in the At box even when that place is no longer offered, as happens once you have crossed there, so a crossing you do not change stays exactly as it was. Arrowing the name box to another runway and back to the step's runway puts the At box back on the step's own place; another runway starts on **(none)**.

To keep the step as it was, press Escape: focus goes back to that step in the Clearance list. If a drop-down list is open, the first Escape only closes the list, and the step is still being changed. Pressing Enter on another step starts changing that one instead, and the first stays as it was.

While a step is being changed, Calculate Route goes ahead only if the boxes still say what the step says. Otherwise you hear *"Step 3 is still being changed. Press Enter to replace it, or Escape to keep it."*, and nothing is calculated or changed. Anything else that changes the Clearance list ends the change silently, with the step left as it was: Clear Steps, Remove Step, Delete, the end of a leg, a crossing put back with Input Alt+Y, a SayIntentions fill, a change of airport, Stop Guidance, or closing the window.

A step you have changed keeps its place in the list when BA later drops the steps you have completed. Steps still cannot be moved up or down: to put one in another place, change the steps from that place onwards, or remove them and enter them again.

### Remove Step and Clear Steps {#taxi-remove-steps}

**Remove Step** (Alt+M) removes the step selected in the Clearance list, or the last step when none is selected. From anywhere but the Clearance list it says which step went, for example *"Removed step 3: cross runway 24L."*, and when it leaves the list empty it adds *"Steps cleared."* With no steps it says *"No step to remove."*

**Clear Steps** (Alt+Z) empties the list and says *"Steps cleared."*

To correct a step you do not need to remove it: change it in place instead (see [Changing a step](#taxi-change-step)).

### How a Progressive Taxi leg ends {#progressive-taxi-controls}

Progressive Taxi has no controls of its own. With no destination, the leg ends at its last step, or at a hold short you entered before any taxiway. See [Progressive Taxi](#taxi-progressive).

### Fill from SayIntentions {#fill-from-sayintentions}

A button that fills the whole planner from your latest SayIntentions taxi clearance: the destination and the Clearance list. It does the same as Input Ctrl+Shift+Y. See [SayIntentions taxi import](#taxi-sayintentions).

### Calculate Route {#calculate-route}

Builds the route from the destination and the steps, and starts guidance immediately. See [Calculating the Route](#taxi-calculating).

### Stop Guidance {#stop-guidance}

Stops taxi guidance and empties the Clearance list. The status line says *"Guidance stopped."*

### Last route summary {#last-route-summary}

A read-only text box at the end of the window (Alt+N) holding the summary of your last route, or the reason the last calculation failed. Arrow through it with your screen reader whenever you want to hear the route again. It is the most reliable way to read the full summary, because the first guidance instruction often begins before BA has finished speaking it.

### Keyboard shortcuts in the planner {#keyboard-shortcuts-in-the-planner}

The planner has Alt shortcuts that jump straight to a control. A button's shortcut presses the button.

<div class="table-wrap" markdown="1">

| Shortcut | Goes to |
|---|---|
| Alt+A | Airport (ICAO) |
| Alt+T | Destination type |
| Alt+G | Gate search |
| Alt+E | Destination |
| Alt+F | Show fitting only |
| Alt+O | Hide occupied stands |
| Alt+I | Runway entry |
| Alt+3 | CAT 3 / low-visibility hold (LVP) |
| Alt+B | Full-length departure (requires backtrack) |
| Alt+K | Step kind |
| Alt+V | Via taxiway: chooses the kind and moves to the name box |
| Alt+L | Left onto taxiway: chooses the kind and moves to the name box |
| Alt+R | Right onto taxiway: chooses the kind and moves to the name box |
| Alt+X | Cross: chooses the kind and moves to the name box |
| Alt+H | Hold short of: chooses the kind and moves to the name box |
| Alt+W | The name box |
| Alt+D | Add Step |
| Alt+U | Clearance |
| Alt+M | Remove Step |
| Alt+Z | Clear Steps |
| Alt+Y | Fill from SayIntentions |
| Alt+C | Calculate Route |
| Alt+S | Stop Guidance |
| Alt+N | Last route summary |

</div>

Every letter belongs to one control only, and Alt+V, Alt+L, Alt+R, Alt+X and Alt+H work from anywhere in the window. The At box has no shortcut: Tab reaches it from the name box. In the Clearance list, Enter changes the selected step and Delete removes it; while a step is being changed, Escape keeps it as it was (see [Changing a step](#taxi-change-step)). In this window plain Alt+Y is Fill from SayIntentions, while the Hold short key is Input Alt+Y (left bracket, then Alt+Y; see [Holding short after all](#holding-short-after-all)).

## Choosing Your Destination {#taxi-destination-types}

### Gate or stand {#gate-or-stand}

Choose **Gate, stand or parking**, then the stand, and enter the steps of your clearance. Use the Gate search box to narrow the list. When you calculate, BA routes you to the stand, lines you up with it, and, if docking guidance is switched on, hands you to docking for the final metres. See [Arriving at a Stand](#taxi-gate-arrival) and [Docking Guidance](gates-gsx-and-docking.md#docking).

If another aircraft is parked on the stand when you calculate, BA warns you, for example *"Warning: BAW123 is at the destination gate."*, or *"Warning: an aircraft is at the destination gate."* if it has no callsign.

With GSX, calculating the route can also prepare the stand in GSX; see [Letting BA Prepare Your Stand in GSX](gates-gsx-and-docking.md#gsx-auto-select).

### Runway, full length {#runway-full-length}

Choose **Runway**, then the runway, leave the Runway entry list on **Full length** and the runway checkboxes unticked, and enter the steps of your clearance. BA takes you to the hold line before the full-length entrance, stops you there, and after you press Continue lines you up at the start of the runway. This is the normal departure.

BA works out the full-length start point from the airport's data, and makes sure the route ends at a real runway entrance. It never silently swaps in an entrance further down the runway, so you can never be handed less runway than you asked for. If no taxiway reaches that point, BA warns you instead of guessing.

### Runway, intersection or named holding point {#runway-intersection-or-named-holding-point}

Choose the runway, then the intersection or holding point in the **Runway entry** list (Alt+I; see [Runway entry](#intersection-departure)). BA routes you there, holds you short, and confirms after you calculate, for example *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."* or *"Holding point A2, runway 28. About 3600 metres of runway ahead."* A holding point partway down the runway is treated like an intersection departure; at a full-length holding point the lineup is still at the full length, and the choice just decides which entrance you use.

Controllers in the UK and much of Europe often say this limit first: "taxi to holding point A1 runway 27 via C". Choose Runway 27, choose **A1 (holding point)** in the Runway entry list, then enter the steps, here Via C. The destination and the steps can be filled in either order.

Any runway destination can also have the **CAT 3 / low-visibility hold (LVP)** box ticked, which only changes where you stop before the runway.

### Runway, full length with a backtrack {#taxi-backtrack-departure}

Choose the runway and tick **Full-length departure (requires backtrack)**. BA finds the entrance nearest the departure end, routes you there and holds you short with *"Stop. Hold short of Runway 32. Press continue when cleared to enter and backtrack."* When ATC clears you onto the runway, press Continue. You hear *"Entering Runway 32. Backtrack to full length, heading 140, then turn around to heading 320."*

The tone now steers you down the runway centreline towards the far end. As you approach it you hear *"Approaching runway end. Slow down, prepare to turn around to heading 320."*, then *"Runway end. Turn around to line up, heading 320."* The tone stays on through the whole turnaround. It pans hard to whichever side you are turning and settles down as you straighten, so silence always means you are lined up, never that BA has given up. You then hear the normal *"Lined up, Runway 32. Hold position."*

If BA cannot find a suitable entrance, it says *"No backtrack entrance found for this runway. Full length departure."* and plans a normal full-length departure.

### Progressive Taxi {#progressive-taxi}

Choose **Progressive Taxi (no destination)** when ATC gives you part of a route at a time, or a clearance with no destination: the leg ends where your steps say. See [Progressive Taxi](#taxi-progressive).

### De-icing area {#deice-area}

Choose **De-icing area** to taxi to a remote de-icing pad. The pads come from GSX airport profiles, so this list is empty at airports with no GSX de-icing data, and you hear *"No deicing areas at this airport."*

### Place {#place}

Choose **Place** to taxi to a building or facility by name: an FBO after landing, a hangar, a fuel island, a terminal or a cargo area. BA takes you to the stand in front of it, with docking included where the scenery gives a stop. The list depends on what BA knows about the airport, from its database, from online map data and from your installed scenery. While it is being built you may hear *"Loading places for KXYZ."* If nothing can be routed to, you hear *"No places to route to at KXYZ."*, and if the data could not be read, *"Places could not be loaded for KXYZ."* The places themselves are described more in [The Surroundings Window](knowing-where-you-are.md#surroundings).

## Entering an ATC Clearance {#taxi-clearances}

This is the heart of taxi guidance. The rule is simple: **enter each instruction as a step, in the order ATC said them.** "Left onto P, cross runway 24L at P, hold short of taxiway L" is three steps: Left onto P, Cross runway 24L at P, Hold short of taxiway L.

BA follows your clearance in order. It does not take a shortcut down some other taxiway just because it is shorter. If you enter Via A, Via B, Via K, the route really does go along A, then B, then K, and then on to your destination. Crossings and hold shorts are placed where your route meets them, so you never have to move them to a different place from the one ATC said them in.

### The basic method {#the-basic-method}

1. Open the planner with Input Shift+Y.
2. Check the airport.
3. Choose the destination type and the destination: the runway or stand ATC gave you, or **Progressive Taxi (no destination)** when ATC gave you none.
4. Set any runway option ATC gave you: an intersection or a named holding point in the Runway entry list (Alt+I), and the low visibility or backtrack box.
5. Enter the first taxiway ATC said: Alt+V, choose it (press its first letter), Enter. You hear it back, for example *"Step 1: via A."*
6. Enter each instruction after it the same way. After each step you are back in the name box on Via taxiway, so another taxiway is just a letter and Enter. For a crossing, Alt+X, choose the runway, Enter (Tab to the At box first if ATC said where). For a hold short, Alt+H, choose the runway, taxiway or holding point, Enter.
7. If ATC named the direction of a turn, choose Left onto or Right onto for that taxiway (Alt+L or Alt+R) instead of Via.
8. Arrow through the **Clearance** list (Alt+U) and check it against what ATC said.
9. Press **Calculate Route** (Alt+C), then listen to the summary, or read it in the **Last route summary** box, and check it matches what ATC said.

You do not need to tell BA to stop before runways: BA stops you before every runway your route crosses or enters, automatically, whether or not ATC mentioned it. Enter a crossing only when ATC has cleared it ("cross runway 22R"): that is what lets BA take you across without stopping. Nothing but a Cross step you entered, or the Continue key, ever clears a runway.

<div class="callout tip" role="note" markdown="1">

**Tip:** You do not have to enter the little unnamed links between stands and taxiways. Leave them out, just as a controller does. BA finds its own way from your stand onto the first taxiway you entered. When that first taxiway is some distance away, the summary tells you how you will reach it, for example *"First taxi onto A."* or *"First taxi via L1 to reach A."*

</div>

### What ATC says and what to enter {#taxi-atc-to-steps}

This table lists common instructions and the step each becomes. The last rows are instructions that need no entry at all.

<div class="table-wrap" markdown="1">

| ATC says | What to enter |
|---|---|
| "Taxi to runway 27 via A, B" | Destination type Runway, Runway 27. Steps: Via A, Via B. |
| "Taxi to stand B12 via K, L" | Destination type Gate, stand or parking, stand B12. Steps: Via K, Via L. |
| "Continue on K", "join B" | Via K, Via B. |
| "Left onto P", "turn right on C" | Left onto P, Right onto C. |
| "Cross runway 24L" | Cross runway 24L. |
| "Cross runway 24L at P" | Cross runway 24L, with P in the At box. |
| "Cross Bravo" (a taxiway) | Cross, taxiway B: a check that your route crosses it. |
| "Hold short of runway 04R" | Hold short of runway 04R. |
| "Hold short of taxiway L" | Hold short of taxiway L. |
| "Hold short of L2", "hold at B2" | Hold short of L2 (holding point). |
| "Hold short of runway 27" for the runway you are taxiing to | Nothing; if you enter it, it adds no second stop. |
| "Taxi to holding point A1 runway 27 via C" | Runway 27, Runway entry A1 (holding point). Steps: Via C. |
| "Taxi to holding point C5 via A" (no runway named) | Progressive Taxi. Steps: Hold short of C5 (holding point) first, then Via A. |
| "Taxi to holding point C5", "follow the greens to C5" | Progressive Taxi. Step: Hold short of C5 (holding point). BA finds the way. |
| "Taxi to runway 27" with no taxiways | Runway 27 and no steps. BA finds the way. |
| "Line up runway 27L at B3" | Runway entry B3. |
| "Low visibility procedures in force" | Tick CAT 3 / low-visibility hold (LVP). |
| "Cross runway 22R", while you are already rolling on your route | Press Input Y once BA has started counting you down to it. |
| "Hold short of runway 04L", for a runway you are cleared across | Press Input Alt+Y. |
| "Give way to the Airbus from the left", "follow the 737", "behind the company aircraft" | No entry. Watch and listen for the traffic yourself, with [Ground Traffic](ground-traffic.md) callouts or Output Alt+G. |
| "Hold position" | No entry. Stop and stay stopped until ATC clears you on. |
| "Expedite" | No entry. |
| "Contact tower 118.3", "monitor ground" | No entry. Change frequency as usual. |

</div>

### Taxiway names you can't find {#taxiway-names-you-can-t-find}

The lists only contain taxiways that exist in your scenery's data. If ATC names a taxiway you cannot find:

- Try the online names setting, if it is off. It fills in names your scenery leaves blank.
- Check you have the right airport, and that your database was built after you installed this scenery.
- Some default sceneries have fewer taxiways than the real airport. You may need to pick the nearest equivalent, or leave that taxiway out and let BA find the way from the step before it.

## Worked Examples {#taxi-examples}

The airports in these examples are made up, but the phraseology is real.

### Example 1: a standard departure with a runway hold short {#example-1-a-standard-departure-with-a-runway-hold-short}

ATC: *"Speedbird 123, taxi to holding point runway 27L via A, B, hold short of runway 22R."*

1. Destination type: **Runway**. Destination: **Runway 27L**.
2. Steps: **Via A**, **Via B**, **Hold short of runway 22R**. That is Alt+V, choose A, Enter; choose B, Enter; Alt+H, choose 22R, Enter. To choose a name, press its first letter or digit, again if another name starting the same way comes first.
3. Calculate Route.

You will hear a summary along the lines of *"Route to Runway 27L via A, B. 1450 metres, crossing runway 22R."* As you near the 22R hold line on B, BA counts you down (see [Hold Shorts and Continue](#taxi-holds)) and stops you with *"Stop. Hold short of runway 22R. Press continue when cleared."* When ATC says "cross runway 22R", press Input Y. You hear *"Continuing. Taxiway B."* and carry on to the 27L hold.

### Example 2: a clearance that includes a crossing {#example-2-a-clearance-that-includes-a-crossing}

ATC: *"Taxi to holding point runway 27L via A, cross runway 22R, B."*

Enter the crossing as a step, where ATC said it: **Via A**, **Cross runway 22R**, **Via B**. That is Alt+V, choose A, Enter; Alt+X, choose 22R, Enter; choose B, Enter.

When you calculate, once guidance has started you hear the crossing named as cleared: *"Cleared across runway 22R."* The **Last route summary** box names it too, along the lines of *"Route to Runway 27L via A, B. 1450 metres, cleared across runway 22R."* BA does not stop you at the 22R hold line, but it never lets you cross in silence: it counts you down to the line, *"Runway 22R in 120 metres, cleared to cross."*, and names it as you cross, *"Crossing runway 22R."* Check the runway is clear as you near it (the [Ground Traffic](ground-traffic.md) callouts or Output Alt+G help if you are unsure about traffic). If ATC takes the crossing back, press Input Alt+Y and BA stops you at the line after all (see [Holding short after all](#holding-short-after-all)).

Enter a crossing only when ATC has actually cleared it. Without the Cross step, BA puts a stop at the 22R hold line, because BA cannot hear ATC and rolling across an active runway unannounced is the most dangerous mistake on an airport; a crossing ATC clears later, while you taxi, you can clear with Input Y as soon as BA starts counting you down to that hold line. See [Continue before the line](#continue-before-the-line).

### Example 3: a taxiway used twice, "A, C, A" {#example-3-a-taxiway-used-twice-a-c-a}

ATC: *"Taxi to runway 09 via A, C, A."* This happens where the controller wants you off A for a short stretch, round a parked aircraft or a closed section, and then back onto it.

1. Destination: **Runway 09**.
2. Steps: **Via A**, **Via C**, **Via A**. For the second A, A is in the name box's list, because only the taxiway of the step just above, C, is left out.
3. Calculate Route.

Check the summary: it should say *"via A, C, A"*. If the second A turns out to be very short, that is just the geometry of the airport.

### Example 4: the same taxiway on both sides of a runway {#example-4-the-same-taxiway-on-both-sides-of-a-runway}

ATC: *"Taxi to runway 22R via N, hold short of runway 15R."* Then later, *"Cross runway 15R, continue on N, hold short of runway 22L."*

For the first call, enter **Via N**, **Hold short of runway 15R**. With only that entered, BA chooses its own way on from 15R, which may leave N. So when the second call comes while you hold at 15R, open the planner, add **Via N** and **Hold short of runway 22L**, and calculate again; then press Input Y when you are cleared across 15R. If the summary (or Output Y) already says your route carries on along N to 22L, you can simply press Input Y instead: BA stops you before 22L, as before every runway.

When you know the whole clearance, enter it in one go: **Via N**, **Hold short of runway 15R**, **Via N**, **Hold short of runway 22L**. After the hold short, **N** is offered again, because the step just above is not a taxiway. Each runway gets its own stop, and Continue at each one clears just that runway.

If the second call clears you across 15R before you have reached it ("cross runway 15R, continue on N, hold short of runway 22L") and you are entering it all at once, enter the crossing instead of the hold short: **Via N**, **Cross runway 15R**, **Via N**, **Hold short of runway 22L**.

### Example 5: an intersection departure {#example-5-an-intersection-departure}

ATC: *"Taxi via A, B3, line up runway 27L at B3."*

1. Destination: **Runway 27L**. In the **Runway entry** list (Alt+I), choose **B3, 2400 metres remaining**: press B until you hear it.
2. Steps: **Via A**, **Via B3**.
3. Calculate Route. You hear *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."*

If you only need a different painted line at the full-length end ("hold short A2"), choose that holding point in the same list instead, **A2 (holding point)**.

### Example 6: a low-visibility departure {#example-6-a-low-visibility-departure}

ATIS: *"Low visibility procedures in force."* ATC: *"Taxi to CAT 3 holding point runway 27L via A, B."*

Enter the route as in Example 1 (Via A, Via B), and also tick **CAT 3 / low-visibility hold (LVP)** (Alt+3). The summary tells you whether a CAT III line was found.

### Example 7: a gate after landing {#example-7-a-gate-after-landing}

ATC, after you vacate: *"Speedbird 123, taxi to stand B12 via K, L."*

1. Stop clear of the runway. If you were using the Landing Exit Planner, it has already told you where you are; see [The Landing Exit Planner](approach-and-landing.md#landing-exit).
2. Open the planner with Input Shift+Y. The airport is filled in.
3. Destination type: **Gate, stand or parking**. In **Gate search**, type **B12**, then choose the stand in **Destination**.
4. Steps: **Via K**, **Via L**.
5. Calculate Route. BA routes you from where you are actually stopped, and at the stand hands you to docking guidance.

If your route crosses a runway on the way in, you will be stopped before it, exactly as on departure.

### Example 8: no taxiways given {#example-8-no-taxiways-given}

ATC: *"Taxi to stand 40."* or you are flying offline and just want to get to the runway.

Choose the destination, enter no steps, and calculate. BA finds the shortest route over the taxiways, still stopping you before every runway. If the Clearance list still holds steps, clear them first with Clear Steps (Alt+Z).

### Example 9: a turn ATC names {#example-9-a-turn-atc-names}

ATC: *"Taxi to runway 33L via F, H, left on C."*

1. Destination: **Runway 33L**.
2. Steps: **Via F**, **Via H**, then **Left onto C**: Alt+V, choose F, Enter; choose H, Enter; Alt+L, choose C, Enter. You hear the last step back as *"Step 3: left onto C."*
3. Calculate Route.

If the airport's taxi data has a left turn from H onto C that your route did not already take, you hear *"Turning left onto taxiway C, as picked."*, in the summary and again once guidance starts. If your route already turned left there, you hear nothing extra.

If the data has no link from H onto C in that direction, BA builds a short off-chart leg and the summary says so, for example *"Note: this route includes an off-chart left turn onto taxiway C, 95 metres with no taxi line."* Guidance then announces the leg when you reach it. See [Off-chart legs](#taxi-off-chart-legs).

### Example 10: a clearance with no destination {#example-10-a-clearance-with-no-destination}

ATC, after you vacate runway 24R onto P: *"Left onto P, cross runway 24L at P, hold short of taxiway L."*

1. Destination type: **Progressive Taxi (no destination)**.
2. Steps: **Left onto P**, **Cross runway 24L at P**, **Hold short of taxiway L**. That is Alt+L, choose P, Enter; Alt+X, choose 24L, Tab, choose P in the At box, Enter; Alt+H, choose L, Enter.
3. Calculate Route.

Once guidance has started you hear *"Cleared across runway 24L at P."*, and the **Last route summary** box reads something like *"Route to hold short of taxiway L via P. 521 metres, cleared across runway 24L at P."* BA counts you down to the 24L hold line without stopping you and names the runway as you cross. It then counts you down to taxiway L and ends the leg before it, far enough back for your whole aircraft to stay clear of L where the route leaves room: *"Hold short of taxiway L. Set a new route when cleared."* Stop and hold there. When ATC gives you the next part, open the planner: the list is empty, ready for it.

### Example 11: the limit first {#example-11-the-limit-first}

ATC, at an airport where the controller says the limit first: *"Taxi to holding point C5 via A."* No runway is named, so this is a leg with no destination.

1. Destination type: **Progressive Taxi (no destination)**.
2. Steps: **Hold short of C5 (holding point)**, then **Via A**. A hold short entered before any taxiway is where the leg ends; the taxiways after it are the way there.
3. Calculate Route. The leg ends at C5: *"Hold at C5. Set a new route when cleared."*

If the controller names the runway too, "taxi to holding point A1 runway 27 via C", make it a runway departure instead: Runway 27, **A1 (holding point)** in the Runway entry list, and the step **Via C**. If you are only told the limit ("taxi to holding point C5", or "follow the greens to C5"), enter only the hold short: BA finds its own way there.

## Changing a Clearance on the Move {#taxi-amend}

ATC often amends a clearance while you are taxiing. Open the planner again with Input Shift+Y. Focus is on **Step kind**, and the Clearance list shows only what is still ahead:

- the taxiways you have finished are gone. On a Progressive leg a finished taxiway can stay, in front of a hold short still ahead that you entered after it, so the hold short does not become where the leg ends. When you calculate, BA does not send you back along that taxiway: you may hear *"Taxiway A is not on the way from here and was left out."*, and the hold short stays wherever your route meets it;
- a runway you have crossed, and a hold short you have passed, are gone;
- a Left onto or Right onto step whose turn you have made reads as Via, so the turn is never judged again;
- a crossing you put back with Input Alt+Y reads as *"Hold short of runway 04L"*, so calculating again does not clear it again;
- a step BA cannot be sure about stays. A taxiway the clearance uses twice ("A, C, A") is followed along your route, so on the second A only that A is left. Once BA has recalculated your route after you went off it, and the new route no longer follows the whole clearance from its first taxiway, BA cannot tell which A you are on: every taxiway step stays, while a runway you have crossed and a hold short you have passed still go.

If nothing is left, you hear *"Steps cleared."* Add the new instruction (for "cross runway 04L", Alt+X, choose 04L, Enter), change any step ATC changed (for "continue via D instead of C", select Via C in the Clearance list, press Enter, choose D, Enter; see [Changing a step](#taxi-change-step)), remove any step ATC cancelled (select it in the Clearance list and press Delete, or press Alt+M, which removes the selected step, or the last step when none is selected), and press Calculate Route (Alt+C). The new route starts from where you are right now, not from where you were when you first opened the window. A Cross step still in the list is a fresh clearance for the new route: it clears the first crossing of that runway ahead, or the one at its At, and you hear it named as cleared once guidance starts.

If your Progressive leg ends just across a runway and you calculate again once you are across, before the leg has ended, you hear *"You are already across runway 24L. Add the next step."* Add the steps ATC gave you next and calculate again. Sometimes BA cannot tell which side of the runway you are now on, and says instead *"Could not tell which side of runway 24L you are on. Add the next step."* This happens when the crossing began on the runway itself or right at its edge (the route was calculated while you were on the runway, or with its first point already across it), when the point BA kept for that crossing lies on the runway's pavement in the airport's data, or when the airport's data has no usable outline for that runway.

## SayIntentions Taxi Import {#taxi-sayintentions}

If you fly with SayIntentions, BA can fill in the planner from the taxi clearance SayIntentions gave you. Press **Input Ctrl+Shift+Y**, or the **Fill from SayIntentions** button in the planner. BA fills in the destination and the Clearance list, and names anything it could not apply, so you can check it against what you heard. Any steps you had entered are replaced.

- Each cleared taxiway becomes a Via step, in the order given. A taxiway the clearance comes back to after a hold short ("N, hold short of runway 15R, N") gets a step each time.
- Each runway hold short becomes a Hold short of runway step, straight after the taxiway it comes after in the clearance. Two hold shorts after one taxiway ("hold short of runways 4L and 4R") get a step each.
- The fill never enters a Cross step, even when the clearance says "cross": only you can clear a runway. When ATC has cleared you across, add the crossing yourself (Alt+X) before you calculate, or press Input Y once BA counts you down to that runway. Nor does it enter Left onto or Right onto: every taxiway it adds is a Via step.

By default the import only fills the form, and you press **Calculate Route** yourself after checking it. If you would rather it started guidance straight away, tick **SayIntentions import starts taxi guidance immediately** in the SayIntentions section of the Taxi Guidance settings tab. After landing, when SayIntentions has assigned you an arrival gate, the import always routes you to a gate, never back to a runway.

The details, including how BA reads the clearance and SayIntentions' own ground track, are in [SayIntentions](networks-and-integrations.md#sayintentions).

## Progressive Taxi {#taxi-progressive}

Some controllers give a clearance one piece at a time: "taxi via A, B, hold short of runway 09", then later "cross runway 09, taxi via C, hold short of taxiway D". Progressive Taxi lets you follow each piece without pretending to know where you will end up.

1. Choose **Progressive Taxi (no destination)** as the destination type. The Destination list and the runway options disappear.
2. Enter the steps of this leg as usual, for example **Via A**, **Via B**, **Hold short of runway 09**. With no step at all, Calculate says *"Enter a step, or choose a destination."*
3. Press **Calculate Route**.

The leg ends at its last step. In the list below, "your last taxiway" is the taxiway of your last Via, Left onto or Right onto step.

- **Hold short of a runway** ("hold short of runway 09"): guidance ends at that runway's hold line on your side, on your last taxiway. With no taxiway step, it ends at the first of that runway's hold lines on your side that you reach.
- **Hold short of a taxiway or a named holding point** ("hold short of taxiway D", "hold short of L2", "hold at VIKAS"): for a taxiway, guidance ends before the first place along your route where your last taxiway meets it away from a runway; with no taxiway step, at the nearest place on that taxiway that you reach, away from a runway. A place on a runway or right beside one is skipped: the leg then ends before a later place where they meet, so your route crosses that taxiway on the way. When the first place is skipped, Calculate tells you so with the warnings at the start, for example *"Taxiway D meets taxiway C first beside runway 27; you will cross D there and hold short of it further on."* Where the route leaves room, the end is far enough back for your whole aircraft to stay clear of that taxiway; how far depends on your wingspan. When that taxiway is closer than that to your turn onto your last taxiway, the leg ends before the turn, on the taxiway before it, whether or not that step is Left onto or Right onto: you make the turn on your next leg. Where a runway you cross lies just before it, the end is close to that taxiway: just past the runway, or where your route meets it. The nose of a large aircraft can then be on that taxiway, not just near its edge, and so it can when the taxiway you stop on carries on and meets it just past the turn. Where your last taxiway meets that taxiway more than once, a Cross taxiway step entered after your last taxiway step picks the place beyond the taxiway it crosses: Via A, Cross taxiway B, Hold short of taxiway C ends before the C beyond B. For a holding point, guidance ends at that point, and you do not need to enter a taxiway first. The holding points come from online data.
- **Cross runway** ("cross runway 09"): BA does not stop you at that runway's hold line, but still counts you down to it and names it as you cross (see [Crossings you are cleared for](#cleared-crossings)), and the leg ends just past it. With a taxiway or holding point in the At box, the leg crosses there. Only that one crossing is cleared: the first time the leg crosses that runway, or the crossing at the place in the At box. If you are already past that crossing, or its stop is also a hold short of a taxiway or holding point you entered there, nothing is cleared, and a later crossing of the same runway keeps its stop. If ATC takes the crossing back, press Input Alt+Y to stop at the hold line after all (see [Holding short after all](#holding-short-after-all)).
- **Via, Left onto or Right onto** ("taxi via C"): guidance ends at the end of that taxiway, the way you are going along it. If that taxiway runs onto a runway, the leg ends at its last point clear of the runway.
- **Cross a taxiway**: a leg cannot end there, and Calculate says, for example, *"A leg cannot end on cross taxiway B. Add where to hold short, or the next taxiway."*

One exception, for controllers who say the limit first ("taxi to holding point C5 via A"): a Hold short of step entered before any taxiway step is where the leg ends, and the steps after it are the way there. Entered alone, BA finds its own way to it, which also serves "follow the greens". Any other Hold short of step, with steps after it, is a stop on the way: guidance stops you there, and Continue (Input Y) takes you on. See [Example 10](#example-10-a-clearance-with-no-destination) and [Example 11](#example-11-the-limit-first).

When your last taxiway step is Left onto or Right onto, a leg that ends at the end of that taxiway, or at a hold short of a taxiway, ends in the picked direction: at the end of the taxiway that way, never on a runway, or before the first place you meet the other taxiway away from a runway that way, clear of it where the route leaves room, as described above. When that place is too close past the turn for your aircraft to fit between them, the leg ends before the turn, just as it would with a Via step, and you hear no "as picked" note, because you do not make that turn on this leg. A leg that would end on a picked turn's off-chart leg (see [Off-chart legs](#taxi-off-chart-legs)) or at its far end, or on the short connector some picked turns are made over, ends where that turn starts instead, whichever of your taxiways the turn is onto, even when that is well back from the taxiway you hold short of, and it keeps ending there if BA has to recalculate the route. Only when there is nowhere to stop before that turn, because the turn starts where your route does or a runway lies just before it, does the leg end on the off-chart leg or connector; you then hear about that turn as usual, the off-chart leg included. If there is no such point that way, you hear the "does not lead to" note described under [warnings in the summary](#warnings-in-the-summary-and-what-to-do), and BA keeps the charted turn. A leg that ends any other way keeps its usual end point, and gives the same note if the picked turn leads away from it.

As you near the end of the leg, BA counts it down with lines like those before a hold short (see [The countdown](#the-countdown)): *"Hold short taxiway D in 120 metres."*, *"Hold short taxiway D in 60 metres. Slow down."* and *"Hold short taxiway D in 30 metres. Stop."* The words name how the leg ends: *"Hold short runway 09"*, *"Hold at VIKAS"*, or *"Route ends"* for a leg that ends across a runway or at the end of your last taxiway. The countdown starts only on the final stretch of taxi line before the end, so you usually hear one or two of these lines, sometimes none, and rarely all three. Usually the leg ends before the *"Stop."* line is due: the end-of-leg sentence below is your cue to stop. Unlike a hold short, a leg has no separate *"Stop."* line when you come to a stop early.

The leg ends when you come within about 25 metres (80 feet) of its end, usually while you are still rolling, or when you stop within about 30 metres (100 feet) of it. A leg that ends at a hold short or a holding point also ends when you stop at any point after *"Slow down."*, so stop where BA tells you to: you do not need to creep up to the end. No leg ends while you are on a runway, and BA never says *"Slow down."* or *"Stop."* for the end of a leg while you are on one: it waits until you are off the runway. A leg that ends across a runway or at the end of your last taxiway also waits while you are right beside a runway.

When the leg ends, the tone stops and you hear one of the sentences listed next. It is your cue to stop: bring the aircraft to a stop and hold there until ATC gives you the next piece.

- *"Hold short of runway 09. Set a new route when cleared."*
- *"Hold short of taxiway D. Set a new route when cleared."*
- *"Across runway 09. Hold position. Set a new route when cleared."*
- *"Hold at VIKAS. Set a new route when cleared."*
- *"Route ended. Hold position. Set a new route when cleared."*

When the leg ends, the Clearance list is emptied. Steps you had already added for the next piece, without calculating, go too; if the planner is open in front of you, you hear *"Steps cleared."* after the end-of-leg sentence, so enter them again. When ATC gives you the next piece, open the planner (focus is on Step kind), enter the next leg's steps, and calculate again. Pressing Continue does nothing at the end of a Progressive leg, because there is no further route to continue along.

Other runways on a Progressive leg are still protected. If the leg crosses any runway you have not entered a Cross step for, BA still stops you before it, and the same goes for any other crossing of a runway you did enter.

If BA cannot find the end point you chose, you hear something like *"Could not find taxiway D from C. Check your entry."*, and for a named holding point you cannot reach, *"Cannot taxi to VIKAS from your position. Check your entry."*

When the end you chose exists, but only on a runway or right beside one, BA says why instead, and calculates nothing: *"There is no place to hold short of taxiway D on C clear of a runway."* when your last taxiway C meets D only there, and *"Taxiway C has no end clear of a runway."* for a leg that ends at the end of C when every end of C is there. Where only some places are there, the leg ends before one that is clear, and when the first place you reach is one of those skipped, Calculate says where you cross the taxiway first, as described above. Check the clearance with ATC. If the stop they mean is that runway's hold line, enter a hold short of that runway instead.

## Calculating the Route {#taxi-calculating}

When you press **Calculate Route**, BA:

1. While a route is active, first drops the steps you have already completed (see [Changing a Clearance on the Move](#taxi-amend)).
2. Takes your aircraft's current position.
3. Builds a route that follows your taxiways in order, ending at your destination, or, with no destination, where your steps say.
4. Adds a stop before every runway the route crosses or enters, placed at the scenery's painted hold line where it has one, and never on a runway, and a stop at every hold short you entered. A crossing you entered as a Cross step then loses its stop, but is still counted down and named.
5. Speaks a summary and writes it into the **Last route summary** box.
6. Starts guidance.

Everything BA says at that moment is one sentence, warnings first, so nothing important is cut off.

### Reading the route summary {#reading-the-route-summary}

A summary has the form *"Route to (destination) via (taxiways). (distance), (runways and holds)."* For example:

- *"Route to Runway 27L via A, B. 1450 metres, crossing runway 22R."*
- *"Route to Gate B 12 - Gate Heavy (Jetway) via K, L. 2.3 kilometres, crossing runways 09L and 09R."*
- *"Route to Runway 04R via C. First taxi onto C. 800 metres."*

What each part means:

- **via ...** lists the taxiways in the order you will use them.
- **then ...** after your taxiways names the ones BA added to reach the destination. You hear it when the taxiways you entered stop short of it: enter only L for Runway 16 and you may hear *"Route to Runway 16 via L, then W, EX23, E, EX33, D. 2.0 kilometres."* Everything before "then" is what you entered; everything after it is BA's own choice of route from there. If ATC gave you the rest of the route, add those taxiways and calculate again.
- **First taxi onto C** or **First taxi via ... to reach C** means your first cleared taxiway is some way from you, and BA has added the bit of apron or link taxiway that gets you onto it.
- **The distance** is the whole route. Short distances are in metres or feet, depending on your setting; long ones switch to kilometres, or nautical miles if you use feet.
- **crossing runway X** and **entering runway X** name every runway the route crosses or goes onto, other than your own departure runway at the end. A runway crossed twice says *"twice"*. Both ends of one runway are named together, such as *"10L/28R"*.
- **cleared across runway X** names a runway crossing you entered as a Cross step, such as *"cleared across runway 04L at C"*: BA counts you down to it but does not stop you there. See [Crossings you are cleared for](#cleared-crossings).
- **N hold short points** counts the other stops you asked for, such as a taxiway hold short.

### Warnings in the summary, and what to do {#warnings-in-the-summary-and-what-to-do}

Most routes produce a clean summary. When something is not right, a warning is added. These are the ones you may meet.

**"... with no hold short point for runway 06L"** (added to the runway list). The route crosses or enters that runway, but there is nowhere safe to stop before it. BA will not stop you there. You must judge that runway yourself: slow down, check with ATC, and listen for traffic. Every other runway named without this warning will get a proper stop. Because guidance starts talking straight away and can cut the summary short, BA also says it again once you are stopped and guidance has started, *"Warning: no hold short point for runway 06L."*, so you hear it whatever happens to the summary. A stop you have already rolled past is never reported missing.

**"Warning: this route does not reach Runway 27L. Check your taxiway entry and reprogram."** You hear this short form; the box holds a longer explanation, such as *"Warning: this route ends about 450 metres to the side of Runway 27L and does not reach the runway. You may be missing the taxiway that connects to the runway."* or *"Warning: this route stops short of Runway 27L ... The last taxiway you entered does not connect to it."* Usually the clearance's last taxiway runs beside the runway, and you have left out the short link taxiway onto it. Add it and calculate again. If you carry on anyway and BA is still far from the centreline during lineup, it repeats: *"This route does not reach Runway 27L. Reprogram the taxi route, including the taxiway that connects to the runway."*

**"Warning: could not follow specified taxiways. Using shortest path. Reason: ..."** BA could not make a route out of the taxiways you entered, for example because two of them never meet, and has used the shortest route instead. The reason is technical, such as *"Step 2: No intersection between 'B' and 'Z'"*, but it tells you which pair failed. Check you picked the right taxiways, in the right order.

**"Warning: route via A, B is 3.2 kilometres; direct route is 900 metres. Check taxiway selection."** The route follows your clearance but is far longer than the direct route, which often means a taxiway was picked by mistake, or was in the wrong order. Sometimes it adds *"Taxiway A is 600 metres from your position."*, meaning your first taxiway is a long way from where you are. If the clearance really is that long, ignore the warning.

**"Note: requested hold-short(s) could not be set — runway 22R (the route does not cross it)."** You entered a Hold short of runway step for a runway the route never crosses or enters. Check the runway and the taxiways you entered. If it says *"(no safe place to hold short after taxiway B)"*, or just *"(no safe place to hold short)"*, the route does meet that runway, but there is nowhere safe to stop before it.

**"Note: requested hold-short(s) not on route — taxiway B (the route does not meet it)."** and **"... holding point L2 (the route does not pass it)."** You entered a Hold short of step for a taxiway or holding point your route never reaches. See [Holding short of a taxiway or a holding point](#hold-short-after-the-first-taxiway).

**"Runway 04L is not crossed at C. It is not cleared; you will be stopped there."** You entered a Cross runway step with a taxiway in the At box, and the route crosses that runway only somewhere else. BA keeps the stop there and will stop you at it. Check the clearance; if the route is right, press Input Y at that stop once ATC clears you. With a holding point in the At box, the sentence names it: *"Runway 04L is not crossed at B3. It is not cleared; you will be stopped there."*

**"The route does not cross runway 04L."** The route never crosses the runway of a Cross step you entered, so nothing was cleared. Check the runway and the taxiways you entered.

**"The route does not cross taxiway B."** You entered a Cross taxiway step, and the route never meets that taxiway. The route is not changed for it. Check the clearance and the taxiways you entered.

**"Taxiway D meets taxiway C first beside runway 27; you will cross D there and hold short of it further on."** Your Progressive leg ends at a hold short of D after taxiway C, and the first place C meets D is right beside a runway, where BA never ends a leg. See [Progressive Taxi](#taxi-progressive).

**"The hold short set on a skipped runway leg was dropped."** Your clearance uses a runway as one of its taxi legs, and you entered a hold short after it. BA cannot route along a runway, so it skips that step, and the hold short goes with it. Enter the hold short after the step before or after, and calculate again.

**"Taxiway E7 is not on the way from here and was left out."** Your clearance starts with a taxiway you can no longer use from where you are. This happens when the clearance was given for one runway exit and you vacated at another: reaching the first taxiway would mean taxiing out to it and turning straight back. BA drops that taxiway and follows the rest of the clearance from your position. A hold short you entered after it is kept, and BA stops you wherever your route meets it. The route summary that follows names the taxiways it kept. You can also hear this while you taxi a Progressive leg, for a finished taxiway the planner kept in front of a hold short (see [Changing a Clearance on the Move](#taxi-amend)).

**"Taxiway E7 is not on the way from here. The route goes out to it and turns back the way it came."** The same situation, but BA keeps the taxiway: it is a Left onto or Right onto step, or the taxiway step after it is (that turn was given for arriving from this taxiway), it is the only taxiway in the clearance, or the rest of the clearance cannot be followed without it. The route stays on the pavement, but it takes you out to that taxiway and straight back the way you came, which may not be possible for your aircraft. Check the clearance with the controller, or remove that step and calculate again.

**"Could not compute a path onto taxiway A along the apron; route starts on A."** BA could not find its own way from your stand to the first taxiway, so the route begins on that taxiway. Taxi onto it carefully; the tone will guide you to it.

**"Your position isn't connected to the taxiway network. The first 40 metres to taxiway A aren't mapped."** Your stand, or the spot you are on, is not joined to the taxiways in the airport's data. BA will steer you straight across that unmapped stretch towards the start of the route, so check it is clear. A similar warning, *"Gate X isn't connected to the taxiway network you're on. The first N of the route aren't mapped."*, means the destination is the part that is cut off.

**"Note: no left turn onto taxiway C was found here. The route keeps the charted turn."** You entered Left onto or Right onto, and BA found no way to make that turn, charted or off-chart. It kept the route it would have built with a Via step. Check the direction and the taxiway of that step, and with ATC if the turn really is possible. You hear the same note when the route includes an uncharted apron crossing.

**"Note: turning left onto taxiway C does not pass holding point A2. The route keeps the charted turn."** The turn is possible, but no way through it passes the named holding point you picked (a holding-point departure, or a Progressive Taxi leg that ends across a runway at a holding point), so BA kept the charted turn. Check which of the two ATC meant: to make that turn, leave the holding point unpicked (Full length in the Runway entry list, or (none) in the At box).

**"Note: turning left onto taxiway C does not lead to Runway 33L. The route keeps the charted turn."** The turn is possible, but nothing beyond it reaches your destination, or only by a loop: doubling straight back, returning the way the route came, coming back onto the same taxiway on the other side of the junction, or running along a runway. BA kept the charted turn. Check the clearance. If ATC really gave you a way round that crosses that taxiway again further on, enter that taxiway as Via, with the clearance's taxiways in order.

**"Note: turning left onto taxiway C would be a long detour to Runway 33L. The route keeps the charted turn."** The turn leads to your destination only the long way round, more than twice the charted route plus 500 metres, so BA kept the charted turn. If ATC really meant that turn, ask whether the long way is intended.

**"Turning left onto taxiway C, as picked."** Not a problem. Your Left onto or Right onto step changed the route, and BA says so, in the summary and again once guidance starts. If you also hear the warning that the route is longer than the direct route, this is usually why.

**"Note: this route includes an off-chart left turn onto taxiway C, 95 metres with no taxi line."** The route includes an off-chart leg. See [Off-chart legs](#taxi-off-chart-legs).

### When a route cannot be built at all {#when-a-route-cannot-be-built-at-all}

If Calculate fails, BA tells you why and puts the same reason in the summary box. Any route you were already following carries on unchanged. Common reasons:

- *"No airport loaded. Enter an ICAO code first."*
- *"Please select a destination."*
- *"Enter a step, or choose a destination."* Progressive Taxi with an empty Clearance list.
- *"A leg cannot end on cross taxiway B. Add where to hold short, or the next taxiway."* A Progressive leg's last step is a Cross taxiway step.
- *"Steps 2 and 4 both name runway 24L, one as a crossing and one as a hold short. Remove one."* The same crossing is in the list twice, once as Cross and once as Hold short of, at the step numbers given (when the two steps use the runway's two ends, both are named, such as *"runway 24L/06R"*). Remove the one ATC did not say.
- *"KK is read from a sign and can only end a leg."* A holding point read from a scenery sign is anywhere but the end of a Progressive leg.
- *"You are already across runway 24L. Add the next step."* Your Progressive leg ends across a runway you have already crossed. Add the steps ATC gave you next.
- *"Could not tell which side of runway 24L you are on. Add the next step."* The same, but BA cannot tell which side you are on now: the crossing began on the runway itself or right at its edge, the point BA kept for it lies on the runway's pavement in the airport's data, or the data has no usable outline for that runway. Add the steps ATC gave you next.
- *"Could not find taxiway D from C. Check your entry."*, *"Cannot taxi to VIKAS from your position. Check your entry."* and the reasons in [Progressive Taxi](#taxi-progressive): BA could not find where your Progressive leg ends.
- *"No taxi route to Gate C 9. This stand can't be reached by the taxi network."*
- *"No taxi route to Gate C 9. It isn't connected to the taxiway network you're on."*
- *"No taxi route from here. You aren't on the connected taxiway network, and the way onto it crosses runway 13."* BA refuses to steer you across a runway on ground its data does not map.
- *"Gate list updated from GSX. Please choose the destination again."* GSX finished loading and the stand list changed. Choose your stand again and recalculate.
- *"No taxi path data available for this airport."*, *"Could not find a nearby taxiway node."* or *"Could not calculate a route to the destination."* Usually missing or broken taxiway data in the scenery.

## Starting Guidance {#taxi-starting}

Guidance starts as soon as the route is calculated. You do not need to press anything else. The status line says *"Route loaded. Guidance active."*

The first thing you hear is one of:

- *"Taxiway A. Steering guidance active."* You are already on your first taxiway.
- *"Steering guidance active. Join taxiway A in 60 metres."* You are on a stand or apron, and the first named taxiway is that far ahead. If you load a route while stopped on a runway, the route starts at an exit further along that runway, your first cleared taxiway if it has one ahead, and the tone leads you down the runway to it. If you roll past that exit, the new route also starts at an exit along the runway.
- *"Steering guidance active."* The route has no named taxiways.

Straight after that, BA may add, in this order:

- For a route filled from SayIntentions, its import summary (see [SayIntentions Taxi Import](#taxi-sayintentions)).
- *"No backtrack entrance found for this runway. Full length departure."*, when you ticked the backtrack box and BA could not plan one.
- Any warning about the route itself, such as a runway with no stop, a step the route does not follow, or a taxiway left out (see [Warnings in the summary, and what to do](#warnings-in-the-summary-and-what-to-do)).
- Each runway crossing you entered as a Cross step, in the order you will cross them: *"Cleared across runway 24L at P."* BA will not stop you there (see [Crossings you are cleared for](#cleared-crossings)).
- Your intersection or holding point, for example *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."*
- Any warning about unmapped ground at the start.
- A warning that the route does not reach the runway. When you hear it, the turn cue below is not said.
- **A turn cue, when the route starts behind you or at a sharp angle.** This is common after a pushback, when you are facing the wrong way. You may hear *"Taxiway A is behind you. Turn left to come around."*, *"Make a U-turn to the right."*, *"Sharp turn left onto taxiway K."* or *"Sharp turn right."* When you are facing almost directly away from the route and every stand nearby is on one side, BA sends you round on that side, over the ramp, even if the other way is slightly shorter.
- *"This runway has no full length taxiway. For the full length, recalculate with the full length departure with backtrack option."*, when you chose a runway marked *"(backtrack required)"* without ticking [Full-length departure (requires backtrack)](#full-length-departure-requires-backtrack).
- Last of all, *"Stop. Hold short of runway 22R. Press continue when cleared."* when you are already standing at a runway hold line that is the route's first stop. You then hear none of the opening lines above, only what BA adds, ending with this. The tone stays silent until you press Continue.

When a warning is spoken at the start, BA holds back its other early callouts for about twelve seconds so they do not talk over it. The tone works throughout.

Now release the parking brake and start rolling. If you are already moving onto your first taxiway, you will hear *"Taxiway A."* when you join it.

## The Steering Tone {#taxi-tone}

The steering tone follows the rules in [The Audio Language: What the Tones Mean](core-concepts.md#audio-language): a fixed pitch, panned to the side you should steer toward, further to the side for a bigger correction (fully to one side at about 30 degrees off course), and silent once you are pointing within a few degrees of where you should be. Steer until it goes quiet; you do not need to centre it exactly. For steering away from the tone, or a hard-panned tone on speakers, see [Taxi Guidance Settings](#taxi-settings).

### Turns {#turns}

The tone looks ahead along the route, so as you approach a turn it starts drifting towards the new direction before you reach the junction, just as a pilot looking out of the window would start the turn a little early. Follow it into the turn.

During a turn, the tone allows for the way the aircraft keeps swinging after you ease off the tiller. It goes quiet slightly *before* your nose reaches the new heading. That is deliberate: when it goes quiet mid-turn, hold that rate of turn and start unwinding, and you will roll out on the line. If it swings to the other side, you are turning too far; unwind.

How early it goes quiet depends on how fast you are turning: the faster the turn, the earlier. In the Fenix A320 family it also depends on how far you have already turned. A big turn, such as a 90-degree corner, goes quiet a little earlier than a small one, because the Fenix carries more swing out of a big turn. Unwind when you hear it go quiet.

After a long turn, once the route ahead is straight, you may hear *"Straighten."* That is your cue to roll out now.

### On the runway: lineup precision {#on-the-runway-lineup-precision}

When you are lining up on a runway, or at a stand, the tone becomes much more precise.

- It stays on until you are within half a degree of the runway heading, and comes back if you drift more than one degree.
- It reaches full pan at 15 degrees instead of 30.
- If you are to the side of the centreline, it does not just point you along the runway; it points you back towards the centreline at an angle (up to 30 degrees), easing off as you get closer, so you arrive on the line already pointing the right way.

**Pulsing tone.** If you stop, or nearly stop (3 knots or less), on the runway while still not lined up, either because you are pointing the wrong way or because you are well off the centreline, the tone pulses on and off three times a second instead of playing steadily. It means "you're stopped and not lined up yet". Follow its direction and roll forward gently; the pulse stops as soon as you are moving.

### When the tone stops {#when-the-tone-stops}

The tone pauses at every hold short (silence there means "stay put", not "on track"), once you are lined up, and when you arrive. It resumes when you press Continue.

<div class="callout tip" role="note" markdown="1">

**Tip:** The tone volume is quiet by default (5 percent), so ATC is easy to hear over it. Adjust it on the Taxi Guidance settings tab, using **Test Tone** to try it.

</div>

## Following the Guidance {#taxi-following}

Here is what happens along the way, and what you do.

### Turns {#turns-2}

For each turn, you normally get two callouts:

1. **Advance notice**, typically 10 seconds ahead at your current speed: *"In 100 metres, turn left onto taxiway B."*, *"In 80 metres, slight right onto taxiway C."*, or for a turn of 60 degrees or more, *"In 150 metres, sharp right onto taxiway D, 90 degrees."* Where the taxiway name changes but the route goes straight on, you hear *"In 100 metres, continue onto taxiway E."*
2. **Turn now**, a few seconds before the turn: *"Left now, taxiway B."*, *"Slight right now, taxiway C."* or *"Sharp right now, taxiway D."*

"Slight" means a turn of between about 20 and 60 degrees. The direction word is worked out from the way you are actually pointing at the time, so it always agrees with the tone, even if you have drifted off line.

When you join the new taxiway you hear its name: *"Taxiway B."*

### Curves {#curves}

Some taxiways bend gradually rather than turning at a junction. When the next 100 metres or so curve by 30 degrees or more, you hear *"Curving left."* or *"Curving right."* Follow the tone round the bend.

### Crossing taxiways {#crossing-taxiways}

When you go straight across another taxiway, you hear *"Crossing taxiway K."*, or *"Crossing taxiways K, L."* for more than one. These help you keep track of where you are, and match the taxiway names a controller may use ("give way to traffic on K"). You can switch them off on the Taxi Guidance settings tab.

### Off-chart legs {#taxi-off-chart-legs}

When you pick a turn and the airport's taxi data has no link that way, BA may build a short straight leg of its own from the end of the previous taxiway to the taxiway you named. There is no taxi line on the ground there. The route summary and the first thing you hear when guidance starts both say, for example, *"Note: this route includes an off-chart left turn onto taxiway C, 95 metres with no taxi line."*

When you reach a leg of 40 metres or more, you hear *"Off-chart leg. No taxi line for the next 95 metres. Follow the tone, then turn left onto taxiway C."* Follow the tone as usual. Nothing is said when the leg ends; the usual turn and taxiway callouts follow. A route can hold more than one off-chart leg.

BA keeps these legs short and safe:

- A leg is never longer than 150 metres, or about 500 feet.
- It stays on pavement the app has mapped.
- It never takes you onto a runway you were not already on.

If you start the route already inside a runway's hold line, and the off-chart leg joins the taxiway there, the runway crossed right after it has no stop of its own. You hear the usual warning that the runway is crossed with no hold short point, so judge that crossing yourself.

### Speed reminders {#speed-reminders}

BA reminds you if you are going too fast:

- *"Slow for turn."* above about 10 knots approaching a turn.
- *"Slow for sharp turn."* above about 8 knots approaching a turn of 60 degrees or more.
- *"Taxi speed, 32 knots."* above 30 knots on a straight taxiway.

These repeat at most every eight seconds. If you would like to hear your speed regularly, turn on **Announce ground speed every** on the Taxi Guidance settings tab.

### Checking where you are {#checking-where-you-are}

At any time:

- **Output Y** tells you your taxiway, your next turn, your next stop and the distance to go, for example *"Taxiway A, right onto taxiway B in 150 metres, hold short of runway 22R in 400 metres, 1200 metres to Runway 27L."* The next stop can be a runway, a taxiway or a holding point. For a runway you are cleared across, it says so instead: *"..., runway 22R in 400 metres, cleared to cross, ..."*, so you know BA will not stop you there. The stop at the very end of your route is not named in the middle, because the sentence ends with it. At a hold it reminds you what you are holding for.
- **Output Ctrl+Y** repeats the last instruction exactly, if another sound drowned it out. If nothing has been said yet you hear *"No taxi instruction yet."*, and with no guidance running, *"No taxi guidance active."*
- **Output Alt+Y** tells you where you are on the airport. See [Where Am I](knowing-where-you-are.md#where-am-i).

### Other traffic {#other-traffic}

Taxi guidance steers you along your route, but it is not what watches for other aircraft. That is the ground traffic monitor, which calls out traffic ahead, on your route and on the runway as you hold. See [Ground Traffic](ground-traffic.md).

## Hold Shorts and Continue {#taxi-holds}

A hold short is a point where guidance stops you and waits for you to say you have been cleared. BA places one:

- before every runway your route crosses or enters,
- at the departure runway itself, and
- at every **Hold short of** step you entered: a runway, a taxiway or a holding point.

A runway crossing you entered as a Cross step, or cleared with Continue before the line, has no stop, but BA still counts you down to it and names it; see [Crossings you are cleared for](#cleared-crossings).

### The countdown {#the-countdown}

As you approach a hold short, BA counts you down in three steps, starting earlier the faster you are going:

1. *"Hold short runway 22R in 120 metres."*
2. *"Hold short runway 22R in 60 metres. Slow down."*
3. *"Hold short runway 22R in 30 metres. Stop."*

If you come to a stop before the last step, you hear *"Hold short runway 22R. Stop."* The runway name may include the taxiway the line is on, such as *"runway 15R at N"*. A taxiway hold reads *"Hold short taxiway B in 60 metres."*, and a holding point *"Hold short holding point L2 in 60 metres."*

### At the line {#at-the-line}

When you reach the line, the tone stops and you hear *"Stop. Hold short of runway 22R. Press continue when cleared."* Bring the aircraft to a stop and set the parking brake if you are waiting. Output Y reminds you: *"Holding short of runway 22R. Press continue when cleared."*

### Continue {#continue}

When ATC clears you across or onto the runway, press **Input Y** (left bracket, then Y). You hear *"Continuing. Taxiway B."*, the tone comes back, and you carry on.

If the route started behind you and you have been holding at its start, the continue callout includes the turn you need, for example *"Continuing. Sharp turn left onto taxiway K."*

<div class="callout warning" role="note" markdown="1">

**Warning:** Only press Continue when you have actually been cleared. BA cannot hear ATC. If you are flying offline, treat every runway hold as a moment to stop, listen for traffic, and check the runway is free before you cross.

</div>

### Continue before the line {#continue-before-the-line}

ATC often clears you across a runway while you are still rolling toward it. You do not have to stop first. Once BA has started counting you down to a runway hold line (*"Hold short runway 22R in 120 metres."*), press **Input Y**. You hear *"Cleared across runway 22R. No stop."*, and BA lets you roll across. It still names the runway at the hold line: *"Crossing runway 22R."* See [Crossings you are cleared for](#cleared-crossings).

- Before BA has started counting you down, Input Y does nothing, so a key pressed by accident never clears a runway you have not been told about.
- For about 2 seconds after the Continue that lets you move off from a hold, Input Y does nothing either, and you hear nothing. A second press to be sure therefore never clears the next runway, even when its hold line is close enough for BA to start counting you down to it at once. If ATC has cleared you across that runway too, press Input Y again after that.
- At a stop that guards two runways, one press clears only the first: *"Cleared across runway 09. Still holding short of runway 01."* If ATC has cleared you across runway 01 as well, press Input Y again: *"Cleared across runway 01. No stop."*, one press per runway. Otherwise BA still stops you at the line for runway 01; when ATC clears you for it, press Continue, and you hear *"Continuing. Crossing runway 09 and runway 01."*, then the taxiway, as you move off.
- It never works at the last stop of your route: the hold for your departure runway, or the end of a Progressive Taxi leg. At the departure runway, stop and press Continue as usual; at the end of a Progressive leg, stop and plan the next leg.
- If the hold line is also a hold short you entered yourself (a Hold short of step for a taxiway or a holding point, at the same place), the press does nothing, and you hear nothing: your own stop stays.
- Pressed by mistake? Press Input Alt+Y to put the stop back (see [Holding short after all](#holding-short-after-all)).

### Holding short after all {#holding-short-after-all}

ATC can take a crossing back while you are on your way to the runway: *"Hold short of runway 04L, traffic on the runway."* A pilot who can see the hold line just stops at it. In BA, press **Input Alt+Y** (left bracket, then Alt+Y). It works on the next runway ahead that you are cleared across, whether you cleared it with Continue or with a Cross step, from the moment the route starts. You hear one of these:

- *"Holding short of runway 04L at C. 80 metres."* You are short of the hold line, and the stop is back, 80 metres ahead along your route. BA counts you down to it like any hold short, then stops you at the line: *"Stop. Hold short of runway 04L at C. Press continue when cleared."* Press Input Y when ATC clears you again. At a stop that guards two runways, the stop asks for both again, one Continue each.
- *"Past the hold line of runway 04L at C."* You are already past the line, or on the runway. Nothing changes: BA never asks you to stop on a runway. Tell ATC where you are.
- When no runway ahead is cleared, it tells you the next stop of any kind and changes nothing: *"Next stop runway 27 in 300 metres."*, *"Next stop taxiway L in 120 metres."*, or *"No runway ahead on this route."* With no route, you hear *"No taxi route."*

The planner follows the key: the Cross step for that runway becomes *"Hold short of runway 04L"* in the Clearance list, so calculating again does not clear it again. If ATC clears you across once more, press Input Y at the line.

Input Alt+Y never clears a runway: only Continue does, or a Cross step when you calculate. Pressed again, it puts back the next runway you are cleared across, if there is one, or tells you what is ahead.

### One stop that guards two runways {#one-stop-that-guards-two-runways}

Sometimes two runways are so close together that there is nowhere to stop between them, so one hold line guards both. BA names both and clears them one at a time, the way a controller would:

1. At the line: *"Stop. Hold short of runway 09 and runway 01. Press continue when cleared for runway 09."*
2. When cleared for 09, press Continue. You hear *"Runway 09 cleared. Still holding. Press continue when cleared for runway 01."* Stay where you are.
3. When cleared for 01, press Continue again, and you move off.

### Two runways close together {#two-runways-close-together}

Where two parallel runways sit close together, ATC often clears you across both at once ("cross runways 28L and 28R at F"), but your route still has a stop before each. When you press Continue at the first and BA sees the second stop a short way ahead, it offers to clear both:

1. Press Continue at the first hold line. You hear something like *"Continuing. Taxiway F. Next hold short of runway 28R at F in 120 metres. Press continue again if cleared across both."*
2. If ATC cleared you across both, press Continue again once you have heard that. You hear *"Also cleared across runway 28R at F. No stop between the runways."*, and BA does not stop you between them. It still counts you down to the second hold line and names it as you cross it.
3. If you were cleared across the first runway only, do nothing. BA stops you at the second hold line as normal.

A second press only counts once the offer has been spoken, about two seconds after the first, so a key that bounces or a quick double tap never clears the second runway by mistake. The offer stands for about twelve seconds. BA makes no offer when the second hold line is also a hold short you set yourself; that stop stays.

### At the departure runway {#at-the-departure-runway}

At the hold for your departure runway you hear *"Stop. Hold short of Runway 27L. Press continue when cleared."* Continue here means "cleared to line up" (or cleared for takeoff). See [Lining Up on the Runway](#taxi-lineup).

## Runway Crossings {#taxi-crossings}

Every runway your route crosses or enters gets a stop, as described above, including your own departure runway if you cross it on the way to its threshold. A recalculated route gets its runway stops too, and a stop you have already rolled past is dropped, so you are never told to stop on a runway you were cleared across.

### Crossings you are cleared for {#cleared-crossings}

A runway crossing you are cleared for has no stop, but BA never lets you cross a runway in silence. A crossing is cleared in two ways only: you enter it as a **Cross** step when you calculate ([Cross and the At box](#taxi-step-cross)), which clears that one crossing, or you press Continue before its hold line ([Continue before the line](#continue-before-the-line)). Nothing else clears one: not the SayIntentions fill, not a recalculation.

- When you calculate, BA names each crossing you entered as cleared, in the order you will cross them, once guidance has started: *"Cleared across runway 04L at C."* The route summary names it too: *"cleared across runway 04L at C"*.
- As you approach it, BA counts you down at the same distances as a hold short, in its own words, with no "Slow down" and no "Stop": *"Runway 04L at C in 120 metres, cleared to cross."*, then *"Runway 04L at C in 60 metres."* So you always know where the hold line is, and can stop there if ATC takes the crossing back (see [Holding short after all](#holding-short-after-all)). After Continue before the line, the countdown has already begun, so you hear no more of it.
- At the hold line you hear *"Crossing runway 04L at C."* If your route starts right at that hold line, there is nothing to count down: you hear *"Crossing runway 04L at C."* as you start to move.
- A clearance is for one crossing, at one place. If your route crosses the same runway again, BA stops you there. If you go off route and the new route crosses that runway somewhere else, BA stops you there too, and "Route changed" says so (see ["Route changed"](#route-changed)).
- **Output Y** tells you whether BA will stop at the next runway ahead (see [Checking where you are](#checking-where-you-are)).

### Reminders and warnings near hold lines {#reminders-and-warnings-near-hold-lines}

BA watches for runway hold lines as you move, whether or not they are on your route.

- *"Crossing runway 22R at B."* A reminder that the hold line just ahead is part of your route, a runway crossing you have planned.
- *"Passing the runway 09L at F hold line."* The route passes a runway hold line but does not then go onto the runway, for example a line set back to protect an ILS beside your taxiway. You are not crossing a runway here.
- *"Warning: approaching runway 36L at M8, off route."* You are heading towards a runway hold line that is **not** on your route, for example because you turned the wrong way or missed a turn. Stop and check where you are with Output Alt+Y. This only speaks for a hold line actually ahead of you, on your heading or on the taxiway you are turning onto, not for lines beside a parallel taxiway you are correctly following. It repeats for the same line no more than every ten seconds.

## Lining Up on the Runway {#taxi-lineup}

At the departure runway, guidance takes you from the hold line onto the centreline.

1. At the hold: *"Stop. Hold short of Runway 27L. Press continue when cleared."* As you approach the runway you may also hear *"Runway 27L ahead."*
2. When ATC says "line up and wait" or "cleared for takeoff", press Input Y. You hear *"Entering Runway 27L, heading 268."*, with *"Turn right."* or *"Turn left."* added if you are more than ten degrees off the runway heading. Where the hold line is set back from the runway and the taxiway runs on before meeting it, you hear *"Continue onto the runway, then turn left."* instead: the tone first follows the taxiway onto the runway, and only then turns you onto the centreline, so you never cut across the grass. If the stop before your runway also guards a runway you cross on the way in, both are named at the hold line, for example *"Hold short of Runway 32 and runway 35."*
3. Follow the precise lineup tone onto the centreline (see [On the runway: lineup precision](#taxi-tone)). Roll forward gently while you turn; if you stop before you are lined up, the tone pulses.
4. When you are within one degree of the runway heading and within about 10 feet (3 metres) of the centreline, the tone stops and you hear *"Lined up, Runway 27L. Hold position."* This is the same position the runway teleport puts you in.

While lining up, Output Y says *"Lining up Runway 27L, runway heading 268. Follow the tone. 40 metres to go."*, and once lined up, *"Lined up Runway 27L, heading 268. Aligned — hold position."*

If you drift out of alignment again before taking off, the tone comes back.

If guidance is replaced by something else while you are lining up, you hear *"Lineup interrupted. No longer aligning with the runway."*

### Handing over to Takeoff Assist {#handing-over-to-takeoff-assist}

When you line up, BA can switch on Takeoff Assist for you. You hear *"Lined up. Activating takeoff assist."*, taxi guidance ends, and the takeoff roll tone takes over. This is controlled by **Auto-activate Takeoff Assist on lineup** on the **Hand Fly** tab in Settings, which is on by default. It fires once per route: if you switch Takeoff Assist off afterwards, it will not switch itself back on until your next route. You can always switch Takeoff Assist on or off yourself with Output Ctrl+T. See [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist).

## Going Off Route and Recalculation {#taxi-off-route}

If you leave your route, for example by missing a turn or taking the wrong taxiway, BA notices and plans a new route from where you are.

### When BA decides you are off route {#when-ba-decides-you-are-off-route}

You count as off route when you are clearly away from the taxiway you should be on for a few seconds while moving. To avoid false alarms, BA does not judge this until you have joined the route at least once (so the taxi from your stand onto the first taxiway never counts), nor during and just after a turn.

### "Route changed" {#route-changed}

When BA builds a new route, you hear *"Route changed. Now via C, D, crossing runway 26R. 900 metres to Runway 27L."* It names the taxiways of the new route and every runway it crosses, so you are never surprised by a hold short. BA keeps as much of your original clearance as still applies, starting from the latest cleared taxiway you are on. The new route gets stops before every runway, just like the original.

If the new route would be exactly the same as the rest of your old one, for example after cutting a corner wide, BA says nothing and simply carries on.

BA does not judge a turn you entered (Left onto or Right onto) again from where you are. It keeps what it decided when the route was loaded:

- If you are still short of the turn, BA rebuilds only the part up to the turn, and keeps the turn and everything after it.
- If you are in the turn, between the junction and the taxiway, the turn counts as missed, and "Route changed" adds *"The left turn onto taxiway C was missed."* If you are already on taxiway C and heading along it, because you cut the corner, the turn counts as made and nothing is said about it.
- Once you have made the turn, no later new route takes you back through that junction. A turn at the very start of the route counts as made only once you are on your way along it, so if you turn the wrong way there, the new route can still take you back through that junction.
- If a new route cannot keep a turn that was still ahead, "Route changed" adds *"The left turn onto taxiway C could not be kept."*

In those two cases BA always takes the new route and says so, even when it uses the same taxiways as the old one: the old route is the one you have left at the turn.

A runway crossing you are cleared for stays cleared on the new route only where the new route crosses that runway at the same hold line. If the new route no longer passes that hold line, BA takes it even when it uses the same taxiways, and if it crosses the runway somewhere else, BA stops you there and "Route changed" adds *"Runway 04L is a stop again."*, before any note about a picked turn, for example *"Route changed. Now via C, D, crossing runway 04L. 900 metres to Runway 27L. Runway 04L is a stop again. The left turn onto taxiway C was missed."*

Calculating a new route in the planner clears all of this.

### When BA cannot recalculate {#when-ba-cannot-recalculate}

- *"Off route. Unable to recalculate."* BA could not find a route from where you are. Stop, check your position with Output Alt+Y, and plan a new route in the planner.
- *"Off route. Could not follow clearance. recalculated route too long. Continuing on original route."* or *"... recalculated route heads backwards ..."* The only new route BA could find would take you far out of your way, or send you back the way you came, so it kept your original route. Get back onto it, or plan a new one. The middle part may instead give a technical reason from the route builder.
- *"Off route. Unable to recalculate. The way to Gate C 9 crosses runway 13."* and similar. The only way to your destination from where you are crosses a runway over ground the airport data does not map, so BA will not steer you there.

## Arriving at a Stand {#taxi-gate-arrival}

As you near your stand, BA counts you down: *"Gate B 12 - Gate Heavy (Jetway) in 15 metres."*, *"10 metres."*, *"5 metres. Stop."* (in feet: 50, 20 and 10). If you stop early, you hear *"Stop. Hold position."*

Then BA turns you to face the stand: *"Align with Gate B 12 - Gate Heavy (Jetway), heading 270."*, with *"Turn left."* or *"Turn right."* if needed. The precise lineup tone guides you round. When you are lined up, you hear *"Aligned with Gate B 12 - Gate Heavy (Jetway). Parking brake."*

In these callouts BA uses the stand's full name exactly as it appears in the destination list, which is why it can be long. The examples below shorten it to "Gate B 12".

### With docking guidance {#with-docking-guidance}

If **Docking guidance (auto-engage at the gate)** is switched on in Settings and the stand has a known stop position, taxi guidance hands over to docking guidance for the last part, which takes you precisely to the stop mark with its own tone, beeps and distances. In that case the callouts change to tell you to keep going:

- *"5 metres. Continue ahead for docking."* or *"Docking guidance ahead. Continue forward."*
- *"Align with Gate B 12, heading 270. Then continue ahead for docking guidance."*
- *"Aligned with Gate B 12. Continue ahead. Docking guidance will take over."*

While docking is in charge, Output Y reports docking's own distance to the stop. See [Docking Guidance](gates-gsx-and-docking.md#docking).

Where a route ends somewhere with no stand heading, such as the end of a taxiway, you simply hear *"(destination) reached."*

### After landing {#after-landing}

If you used the Landing Exit Planner, exit guidance ends once you have left the runway, with a message such as *"Off the runway at B4. Stop and hold position. Open the taxi planner to set a route to your gate."* That is your cue to open the planner (Input Shift+Y) and enter your taxi-in clearance, as in [Example 7](#taxi-examples). After backtracking off a runway you hear *"Runway vacated. No route set — use the taxi planner for a route to your stand."* See [The Landing Exit Planner](approach-and-landing.md#landing-exit) for everything that happens between touchdown and that point.

## Taxi Routes in the Route Briefing {#taxi-briefing}

The AI route briefing also previews your expected taxi out and taxi in. It is a briefing, not live guidance; see [The AI Route Briefing](planning-your-flight.md#planning-route-briefing).

## Hotkeys During Taxi {#taxi-hotkeys}

These keys are the same on every aircraft. For how Output and Input modes work, see [Output Mode and Input Mode](getting-started.md#hotkey-modes).

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| Input Shift+Y | Open the taxi planner |
| Input Y | Continue past the current hold short, or clear the runway ahead once BA has counted you down to it |
| Input Alt+Y | Hold short of the next cleared runway |
| Input Ctrl+Y | Stop taxi guidance (you hear *"Taxi guidance stopped."*) |
| Input Ctrl+Shift+Y | Build a taxi route from your latest SayIntentions clearance |
| Input Shift+X | Open the Landing Exit Planner |
| Output Y | Taxi status: taxiway, next turn, next stop (a runway, a taxiway or a holding point) and, for a runway, whether you are cleared across it, distance to go |
| Output Ctrl+Y | Repeat the last taxi instruction |
| Output Alt+Y | Where Am I: your taxiway, stand or runway |
| Output Alt+L | Look Around: what is near you |
| Output Ctrl+Shift+L | The Surroundings window |
| Output Alt+G | Nearest ground traffic |
| Output Ctrl+T | Switch Takeoff Assist on or off |

</div>

## Taxi Guidance Settings {#taxi-settings}

Open Settings from the File menu and choose the **Taxi Guidance** tab.

- **Steering tone type**, **Volume** (default 5 percent), **Test Tone**, **Invert steering tone direction (steer AWAY from the tone)** and **Hard-pan steering tone (full left or full right; speaker-friendly)** — the tone settings described in [Settings that change the tones](core-concepts.md#audio-language).
- **Announce crossing taxiways (e.g., "Crossing taxiway Link 53")** — on by default. Untick for quieter taxiing.
- **Announce ground speed every:** and **During takeoff assist, announce ground speed every:** — see [Ground speed](knowing-where-you-are.md#where-ground-speed).
- **Use feet for distances (default: metres)** — every taxi distance in feet instead of metres. This covers turns, hold short countdowns, stands, runway exits and the status readouts.
- **Show ground traffic distances in metres (default: feet)** — a separate setting for the ground traffic callouts only. See [Ground Traffic](ground-traffic.md).
- **Auto-select arrival gate in GSX on route calculation** — see [Gates, GSX and Docking](gates-gsx-and-docking.md).
- **Docking guidance** settings — see [Docking Guidance](gates-gsx-and-docking.md#docking).
- **Refresh Taxiway Names**, **Online taxiway, gate and airport building names (OpenStreetMap + X-Plane)** and **Read installed scenery for airport buildings (offline)** — where BA gets names from. See [Knowing Where You Are](knowing-where-you-are.md).
- **Announce airport buildings as you taxi past them** and **Tell me when I leave the paved surface** — optional extra callouts while taxiing, such as *"Passing Concourse B, on the left."* and *"Off the pavement, on grass."* See [Callouts While You Taxi](knowing-where-you-are.md#where-taxi-callouts) and [The Off-the-Pavement Alerts](knowing-where-you-are.md#where-off-pavement).
- **SayIntentions import starts taxi guidance immediately** — see [SayIntentions Taxi Import](#taxi-sayintentions).

Two related settings live elsewhere:

- **Auto-activate Takeoff Assist on lineup**, on the **Hand Fly** tab, on by default.
- The sound device your guidance tones play on, on the **Audio** tab.

## Everything Taxi Guidance Says {#taxi-callouts}

This is a reference list of every taxi guidance callout, grouped by when you hear it. Names, numbers and distances are examples. Where a stand is named, BA uses its full name from the destination list; it is shortened to "Gate B 12" here.

### When you plan and start a route {#when-you-plan-and-start-a-route}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Full length holding point A1."* | The painted line a normal departure from this runway uses (heard when you choose a runway). | Check it matches your clearance; if not, choose the holding point ATC named in the Runway entry list. |
| *"CAT three hold A3."* | With the LVP box ticked, the low-visibility line for this runway. | Nothing; for information. |
| *"B3 is no longer offered. Full length departure."* | The entry you chose in the Runway entry list is no longer offered, and the list is back on Full length. | Check the list: choose the entry ATC gave you if it is there, or keep Full length. |
| *"No deicing areas at this airport."* | No GSX de-icing pads here. | Choose another destination type. |
| *"Step 2: cross runway 24L at P."* | The step you just added or changed, read back once. | Enter the next step, or press Calculate Route. |
| *"Choose a name first."* | You pressed Enter, Add Step or Replace step with nothing chosen in the name box. | Choose the name, then press Enter. |
| *"Step 3 is still being changed. Press Enter to replace it, or Escape to keep it."* | You pressed Calculate Route while changing step 3, with something else in the step boxes. | Press Enter to replace the step, or Escape to keep it, then calculate. |
| *"Removed step 3: cross runway 24L."* | Remove Step took that step out of the Clearance list. | Nothing, if that is the one you meant. |
| *"No step to remove."* | Remove Step found the Clearance list empty. | Nothing. |
| *"Steps cleared."* | The Clearance list is now empty: you cleared it, removed its last step, opened the planner after every step was completed, or the leg ended with the planner open while it held steps you had not calculated yet. | Enter the next steps when ATC gives them. |
| *"Route to Runway 27L via A, B. 1450 metres, crossing runway 22R."* | The route summary. | Check it matches your clearance. |
| *"Enter a step, or choose a destination."* | Progressive Taxi with no steps. | Enter the clearance's steps. |
| *"A leg cannot end on cross taxiway B. Add where to hold short, or the next taxiway."* | A Progressive leg cannot end on a Cross taxiway step. | Add the hold short or taxiway ATC gave you next. |
| *"Steps 2 and 4 both name runway 24L, one as a crossing and one as a hold short. Remove one."* | The same crossing is a Cross step and a Hold short of step, at those step numbers. | Remove the one ATC did not say. |
| *"KK is read from a sign and can only end a leg."* | A point from a scenery sign is entered anywhere but the end of a Progressive leg. | Make it the last step, or choose another point. |
| *"You are already across runway 24L. Add the next step."* | Your Progressive leg ends across a runway you have already crossed. | Add the steps ATC gave you next. |
| *"Could not tell which side of runway 24L you are on. Add the next step."* | Your Progressive leg ends across a runway you have crossed, and BA cannot tell which side you are on now: the crossing began on or right at the runway, or the airport's data does not show which side its point is on. | Add the steps ATC gave you next. |
| *"The route does not cross taxiway B."* | A Cross taxiway step your route never meets. | Check the clearance and your steps. |
| *"Warning: this route does not reach Runway 27L. Check your taxiway entry and reprogram."* | The route ends away from the runway. | Add the missing link taxiway and recalculate. |
| *"Warning: BAW123 is at the destination gate."* | Your stand is occupied. | Check with ATC; choose another stand if needed. |
| *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."* | Confirms your intersection. | Check the runway length is enough. |
| *"Holding point A2, runway 28. About 3600 metres of runway ahead."* | Confirms your named holding point. | Nothing. |
| *"No backtrack entrance found for this runway. Full length departure."* | No entrance suited to a backtrack. | Plan a normal departure. |
| *"Note: this route includes an off-chart left turn onto taxiway C, 95 metres with no taxi line."* | A picked turn needs a short leg with no taxi line. | Follow the tone along it; see [Off-chart legs](#taxi-off-chart-legs). |
| *"Note: no left turn onto taxiway C was found here. The route keeps the charted turn."* | Your Left onto or Right onto step could not be made. | Check the step against the clearance. |
| *"Note: turning left onto taxiway C does not lead to Runway 33L. The route keeps the charted turn."* | The picked turn does not reach your destination. | Check the clearance. |
| *"Note: turning left onto taxiway C would be a long detour to Runway 33L. The route keeps the charted turn."* | The picked turn reaches your destination only the long way round. | Ask ATC whether the long way is intended. |
| *"Note: turning left onto taxiway C does not pass holding point A2. The route keeps the charted turn."* | The picked turn and the picked holding point do not go together. | Check the clearance; pick one of the two. |
| *"Turning left onto taxiway C, as picked."* | Your Left onto or Right onto step changed the route. | Nothing; for information. |
| *"Cleared across runway 04L at C."* | Once guidance has started: a crossing you entered as a Cross step, one sentence for each, in the order you will cross them. BA counts you down to it but does not stop you. | Check it matches your clearance. |
| *"... cleared across runway 04L at C."* | In the summary: the same crossings. | Check it matches your clearance. |
| *"Runway 04L is not crossed at C. It is not cleared; you will be stopped there."* | The runway of your Cross step is crossed only somewhere else than its At. | Check the clearance; press Input Y at that stop once cleared. |
| *"The route does not cross runway 04L."* | The route never crosses the runway of your Cross step. | Check your entry. |
| *"Taxiway A. Steering guidance active."* | Guidance has started; you are on A. | Start taxiing. |
| *"Steering guidance active. Join taxiway A in 60 metres."* | Guidance has started; A is ahead. | Taxi onto A, following the tone. |
| *"Taxiway A is behind you. Turn left to come around."* | The route starts behind you. | Turn round in the direction given. |
| *"Make a U-turn to the right."* | The same, with no taxiway name. | Turn round to the right. |
| *"Sharp turn left onto taxiway K."* or *"Sharp turn left."* | The route starts at a sharp angle. | Make the turn. |
| *"Your position isn't connected to the taxiway network. The first 40 metres to taxiway A aren't mapped."* | You start off the mapped taxiways. | Taxi carefully onto the route, checking it is clear. |

</div>

### While taxiing {#while-taxiing}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Taxiway B."* | You have joined taxiway B. | Nothing; for information. |
| *"In 100 metres, turn left onto taxiway B."* | A turn is coming. | Get ready, slow if needed. |
| *"In 80 metres, slight right onto taxiway C."* | A gentle turn (20 to 60 degrees) is coming. | Get ready. |
| *"In 150 metres, sharp right onto taxiway D, 90 degrees."* | A big turn, with its angle. | Slow well before it. |
| *"In 100 metres, continue onto taxiway E."* | The name changes but you go straight. | Carry straight on. |
| *"Left now, taxiway B."* | Start the turn now. | Turn, following the tone. |
| *"Off-chart leg. No taxi line for the next 95 metres. Follow the tone, then turn left onto taxiway C."* | A short leg with no taxi line starts here. | Follow the tone. |
| *"Curving left."* | The taxiway bends gradually. | Follow the tone round. |
| *"Straighten."* | You have turned enough; the route is straight ahead. | Roll out of the turn. |
| *"Crossing taxiway K."* | You are going straight across K. | Nothing; for information. |
| *"Slow for turn."* / *"Slow for sharp turn."* | Too fast for the coming turn. | Slow down. |
| *"Taxi speed, 32 knots."* | Faster than 30 knots. | Slow down. |
| *"Runway 27L ahead."* | Your destination is close. | Get ready to stop. |
| *"Crossing runway 22R at B."* | A runway crossing on your route is just ahead, or you are at the hold line of a runway you are cleared across. | Make sure you are cleared. |
| *"Runway 04L at C in 120 metres, cleared to cross."* | The first countdown call for a runway you are cleared across; BA will not stop you there. | Check the runway is clear as you near it. |
| *"Runway 04L at C in 60 metres."* | The next countdown call for that runway. | The same. |
| *"Warning: approaching runway 36L at M8, off route."* | You are heading for a runway that is not on your route. | Stop, check your position. |

</div>

### At hold shorts {#at-hold-shorts}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Hold short runway 22R in 120 metres."* | A runway hold is coming. | Prepare to stop. |
| *"Hold short runway 22R in 60 metres. Slow down."* | It is close. | Slow down. |
| *"Hold short runway 22R in 30 metres. Stop."* / *"Hold short runway 22R. Stop."* | Stop now. | Stop. |
| *"Stop. Hold short of runway 22R. Press continue when cleared."* | You are at the line; the tone is paused. | Wait for clearance, then Input Y. |
| *"Stop. Hold short of taxiway B. Press continue when cleared."* or *"Stop. Hold short of holding point L2. Press continue when cleared."* | A Hold short of step you entered. | Wait for clearance, then Input Y. |
| *"Stop. Hold short of runway 09 and runway 01. Press continue when cleared for runway 09."* | One stop guards two runways. | Input Y when cleared for 09. |
| *"Runway 09 cleared. Still holding. Press continue when cleared for runway 01."* | First runway cleared; still waiting. | Stay put; Input Y when cleared for 01. |
| *"Continuing. Taxiway B."* | You pressed Continue; guidance resumes. | Taxi on. |
| *"Continuing. Crossing runway 09 and runway 01. Taxiway K."* | You pressed Continue at a stop guarding two runways, one of which you had already cleared. | Taxi on, across both. |
| *"Cleared across runway 22R. No stop."* | You pressed Continue before the hold line. | Taxi on; BA will not stop you there. |
| *"Cleared across runway 09. Still holding short of runway 01."* | You pressed Continue before a stop guarding two runways; only the first is cleared. | Stop at the line; Input Y when cleared for 01. |

</div>

### Lining up and departing {#lining-up-and-departing}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Stop. Hold short of Runway 27L. Press continue when cleared."* | At your departure runway hold. | Input Y when cleared to line up. |
| *"Entering Runway 27L, heading 268. Turn right."* | You are cleared on; turn onto the runway. | Follow the lineup tone. |
| *"Lined up, Runway 27L. Hold position."* | You are on the centreline, aligned. | Wait for takeoff clearance. |
| *"Lined up. Activating takeoff assist."* | Takeoff Assist is taking over. | Prepare for takeoff. |
| *"Lineup interrupted. No longer aligning with the runway."* | Lineup guidance was stopped by a new route. | Check what you intended. |
| *"This route does not reach Runway 27L. Reprogram the taxi route, including the taxiway that connects to the runway."* | You are far from the runway centreline during lineup. | Stop and plan a new route. |
| *"Stop. Hold short of Runway 32. Press continue when cleared to enter and backtrack."* | Backtrack departure hold. | Input Y when cleared to backtrack. |
| *"Entering Runway 32. Backtrack to full length, heading 140, then turn around to heading 320."* | Taxi down the runway to its end. | Follow the tone. |
| *"Approaching runway end. Slow down, prepare to turn around to heading 320."* | The turnaround is close. | Slow down. |
| *"Runway end. Turn around to line up, heading 320."* | Turn around now. | Follow the tone through the turn. |

</div>

### Progressive Taxi {#progressive-taxi-2}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Hold short taxiway D in 120 metres."* | The end of the leg is coming. It is named the way the leg ends: *"Hold short runway 09"*, *"Hold at VIKAS"*, or *"Route ends"* across a runway or at the end of your last taxiway. You usually hear one or two of these three lines, sometimes none, rarely all three. | Prepare to stop. |
| *"Hold short taxiway D in 60 metres. Slow down."* | The end of the leg is close. | Slow down. |
| *"Hold short taxiway D in 30 metres. Stop."* | Stop now. Usually the leg ends before this line is due. | Stop. |
| *"Hold short of runway 09. Set a new route when cleared."* | End of the leg, often while you are still rolling. | Stop and hold; plan the next leg when ATC gives it. |
| *"Hold short of taxiway D. Set a new route when cleared."* | End of the leg, often while you are still rolling. | Stop and hold; plan the next leg. |
| *"Across runway 09. Hold position. Set a new route when cleared."* | You are across the runway you were cleared over. | Stop and hold; plan the next leg. |
| *"Hold at VIKAS. Set a new route when cleared."* | At the named holding point. | Stop and hold; plan the next leg. |
| *"Route ended. Hold position. Set a new route when cleared."* | End of your last taxiway. | Stop and hold; plan the next leg. |
| *"There is no place to hold short of taxiway D on C clear of a runway."* | C meets D only on or beside a runway, where BA never stops you for a taxiway. No route was calculated. | Check with ATC; end the leg another way. |
| *"Taxiway C has no end clear of a runway."* | Every end of C is on or beside a runway. No route was calculated. | Check with ATC; end the leg another way. |
| *"Taxiway D meets taxiway C first beside runway 27; you will cross D there and hold short of it further on."* | When you calculate: your last taxiway C first meets D right beside runway 27, where BA never ends a leg, so your route crosses D there and the leg ends before a later place they meet. | Check with ATC that the later place is the one they mean. |

</div>

### Arriving {#arriving}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Gate B 12 in 15 metres."*, *"10 metres."*, *"5 metres. Stop."* | Distance to the stand. | Slow and stop. |
| *"Stop. Hold position."* | You stopped before the last step. | Stay stopped. |
| *"Align with Gate B 12, heading 270. Turn left."* | Turn to face the stand. | Follow the tone. |
| *"Aligned with Gate B 12. Parking brake."* | You are parked and aligned. | Set the parking brake. |
| *"5 metres. Continue ahead for docking."* / *"Docking guidance ahead. Continue forward."* | Docking guidance will take you the rest of the way. | Keep rolling slowly. |
| *"Aligned with Gate B 12. Continue ahead. Docking guidance will take over."* | Aligned; docking takes over. | Follow docking guidance. |
| The destination's name, then *"reached."* | You have arrived somewhere with no stand heading. | Stop. |

</div>

### Off route {#off-route}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Route changed. Now via C, D, crossing runway 26R. 900 metres to Runway 27L."* | A new route from where you are. | Follow it; check it agrees with ATC. |
| *"The left turn onto taxiway C was missed."* or *"... could not be kept."* | Added to "Route changed" when a picked turn is gone. | Check the new route against ATC. |
| *"Runway 04L is a stop again."* | Added to "Route changed": the new route crosses a runway you were cleared across somewhere else, and stops you there. | Stop at that hold line; check with ATC. |
| *"Off route. Unable to recalculate."* | No new route could be found. | Stop and plan a route. |
| *"Off route. Could not follow clearance. recalculated route too long. Continuing on original route."* | The new route would be a big detour, so it kept the old one. | Return to your route or plan a new one. |
| *"Off route. Unable to recalculate. The way to Gate C 9 crosses runway 13."* | The only way crosses an unmapped runway. | Stop and ask ATC. |

</div>

### Hotkey answers {#hotkey-answers}

<div class="table-wrap" markdown="1">

| You hear | When |
|---|---|
| *"Taxiway A, right onto taxiway B in 150 metres, hold short of runway 22R in 400 metres, 1200 metres to Runway 27L."* | Output Y while taxiing. For a runway you are cleared across: *"..., runway 22R in 400 metres, cleared to cross, ..."*. |
| *"Holding short of runway 04L at C. 80 metres."* | Input Alt+Y: the stop at a runway you were cleared across is back. |
| *"Past the hold line of runway 04L at C."* | Input Alt+Y once you are past that hold line or on the runway. Nothing changes. |
| *"Next stop taxiway L in 120 metres."* | Input Alt+Y with no cleared runway ahead: the next stop of any kind. |
| *"No runway ahead on this route."* | Input Alt+Y with no stop ahead. |
| *"No taxi route."* | Input Alt+Y with no route. |
| *"Holding short of runway 22R. Press continue when cleared."* | Output Y at a hold. |
| *"Lining up Runway 27L, runway heading 268. Follow the tone. 40 metres to go."* | Output Y while lining up. |
| *"Backtracking Runway 32 to full length. 600 metres to go, then turn around to heading 320."* | Output Y during a backtrack departure. |
| *"Arrived at Gate B 12."* | Output Y after arriving. |
| *"No route loaded."* | Output Y when guidance has nothing to follow. |
| *"No taxi guidance active."* | Output Y or Ctrl+Y with guidance off. |
| *"No taxi instruction yet."* | Output Ctrl+Y before anything has been said. |
| *"Taxi guidance stopped."* | Input Ctrl+Y. |

</div>

## If Something Goes Wrong {#taxi-troubleshooting}

**I can't hear the tone.** The tone is silent whenever you are on track, at a hold and once lined up, so silence is usually good news. Check the volume and use Test Tone on the Taxi Guidance tab. Check which device it plays on, on the Audio tab. If you have a hold-short cue, press Continue only once cleared; the tone resumes then.

**The tone tells me to turn the opposite way to the words.** It should not: both are worked out from your actual heading. If you have the **Invert steering tone direction** box ticked, the tone means steer away from it.

**I followed the tone and ended up going in circles after a wide turn.** Stop, press Output Y to hear where you are, and if necessary recalculate from the planner. Guidance always picks up your real position on the taxiway again.

**The name box already shows a taxiway ATC did not say.** For the first step, BA guesses the nearest taxiway in front of you. Always choose the first taxiway yourself before you press Enter.

**The planner opened with fewer steps than I entered.** While a route is active, the Clearance list shows only what is still ahead, so the steps you have completed are gone. If you hear *"Steps cleared."*, every step was completed. See [Changing a Clearance on the Move](#taxi-amend).

**My steps were gone when I opened the planner.** The list is emptied when the leg it was calculated for is over (you arrived, your Progressive leg ended, you stopped guidance or you landed) and when you change airport, so a clearance never carries over to the next route. Enter the new clearance.

**A taxiway ATC named isn't in the list.** Your scenery's data does not have it under that name. Switch on online taxiway names, rebuild your database if you have changed scenery, or pick the nearest equivalent. See [Taxiway names you can't find](#taxi-clearances).

**The summary says the route does not reach the runway.** The last taxiway you entered runs beside the runway without joining it. Add the short link onto the runway as the last taxiway.

**The summary says it used the shortest path.** Two of your taxiways do not meet in the scenery's data, or they are in the wrong order. Check each pair; the reason names the pair that failed.

**I'm stopped at a hold line and nothing happens.** BA is waiting for you. Press Input Y when cleared. At a Progressive Taxi end point, plan the next leg instead.

**A Progressive leg ended before I reached the taxiway I hold short of.** That is by design: where the route leaves room, BA sets the end far enough back for your whole aircraft to stay clear of that taxiway, and ends the leg about 25 metres (80 feet) before that point, or where you stop near it. When the taxiway you hold short of is very close past your turn onto the last taxiway, the leg ends before the turn, on the taxiway before it, whether that step is Via, Left onto or Right onto. When a runway you cross lies just before it, the end is close to that taxiway, and the nose of a large aircraft can be on it. Either way, the end-of-leg sentence is your cue to stop.

**BA says there is no place to hold short of a taxiway clear of a runway.** At the end of a Progressive leg (*"There is no place to hold short of taxiway D on C clear of a runway."*), every place your last taxiway C meets D is on a runway or right beside one; where only some are, BA skips those and ends the leg before a later one, and when it skips the first, Calculate says so (*"Taxiway D meets taxiway C first beside runway 27; you will cross D there and hold short of it further on."*). For a Hold short of taxiway step on the way (*"Note: no place to hold short of taxiway B clear of the runway; no stop will be called there."*), the first place your route meets B after the taxiway before that step is on a runway, right beside one, or just past one, and BA places no stop for B at all. Either way, check the clearance with ATC; if the stop they mean is the runway's hold line, enter a hold short of that runway instead.

**BA stopped me at a runway ATC had already cleared me across.** That is by design: BA cannot hear ATC. Press Input Y and carry on. Next time, press Input Y as soon as BA has counted you down to that runway (*"Hold short runway 22R in 120 metres."*), and it will not stop you there; see [Continue before the line](#continue-before-the-line). When the crossing is part of the clearance you enter, enter it as a Cross step (Alt+X) before you calculate, and BA does not stop you there at all.

**Calculate says two steps both name a runway, one as a crossing and one as a hold short.** You have a Cross step and a Hold short of step for the same crossing, at the two step numbers it gives. ATC gave you one or the other: remove the one ATC did not say (select it in the Clearance list and press Delete).

**ATC told me to hold short of a runway I was already cleared across.** Press Input Alt+Y. If you are still short of the hold line, BA puts the stop back and stops you there; if you are past it, it tells you so. See [Holding short after all](#holding-short-after-all).

**I heard "Route changed" when I didn't expect it.** You strayed far enough from the route for BA to replan. Check the new taxiways against your clearance; if they are wrong, stop and recalculate from the planner with the right ones.

**I closed the planner and guidance is still running.** Closing the window only hides it. Use Input Ctrl+Y or Stop Guidance to stop.

**The stand I need isn't in the list.** Untick **Show fitting only** and **Hide occupied stands**, and clear the **Gate search** box. If the stand is listed with "(no taxi route)", the scenery's taxiways do not reach it; teleporting there may be the only option (see [Teleporting to a Runway or Gate](planning-your-flight.md#teleport)).

**Distances are in the wrong units.** Use **Use feet for distances** on the Taxi Guidance tab. Ground traffic distances have their own setting.

For anything else, see [Logs and Getting Help](getting-started.md#logs-and-help). Taxi guidance writes its own logs, which help a lot when reporting a problem at a particular airport.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Knowing Where You Are](knowing-where-you-are.md)
- [Next chapter: Ground Traffic](ground-traffic.md)
- [Guide contents](index.md)

</nav>
