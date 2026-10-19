# Return window: English story review

Arrival: 08 SEP 1980. Playable dates: 25 AUG-13 SEP 1981. Twenty dated Earth-standard days in twenty active minutes. See ArchiveBasis.txt for source facts and fictional adaptations.

## intro (story reading)

**System:** ARRIVAL RECORD / 08 SEP 1980.
NOW / 25 AUG 1981. After months at a remote survey station, you return to the colony. Nobody comes to meet you.

**Player:** Colony control, this is the return craft. Does anyone read me?

**AI:** All inhabited sectors are silent. No crew transponders. No response from the surface relay.

**Player:** Then I want to go home.

**AI:** Checking propulsion, reserves and the departure assembly. There is a problem.

- SHOW ME THE DAMAGE (`diagnose`) -> `window`

## window (story reading)

**AI:** The damaged propulsion system has very little fuel margin. Rebuild the departure assembly, and we can still reach a low-energy Earth-return trajectory.

**AI:** Our navigation archive lists 03-13 SEP 1981 for the planned Mars-to-Earth departure. The lower-energy route is the only one inside our remaining fuel budget. Missing our cutoff would demand more fuel than we can safely commit.

**AI:** The next return period listed in the study is 09 NOV 1983 - 19 NOV 1983: more than two years later. It is a planning reference, not a guarantee for this damaged craft. Our stored food cannot cover that wait. We would have to restore the greenhouse.

**AI:** The amber instrument marks our LATEST DEPARTURE authorization. The circled date is 13 SEP 1981; the ascent allowance is already reserved. If we repair early, we can launch to a waiting orbit, then make the Earth-return burn inside the planned window.

- MAKE A PLAN (`make_schedule`) -> `schedule`

## schedule (story reading)

**AI:** The paper calendar runs from 25 AUG 1981 through 13 SEP 1981: 20 dated workdays. The cells use MONTH/DAY, not numbered mission days. I will add a red cross after each date has ended. Only the final deadline is circled.

**System:** The simulation compresses 20 Earth-standard 24-hour dates into 20 minutes of active work. Explicit story reading pauses the mission and all work. Repairs, salvage and ordinary terminal queries use time.

**AI:** The countdown begins when you select BEGIN MISSION. Completing repairs will not launch the craft. You must return here and confirm departure before the clock reaches zero.

**System:** MOUSE: look around. E: connect or leave this terminal. ESC: return from a workstation, close the terminal, or pause the mission.

- BEGIN MISSION (`start_mission`) -> `mission`
- REVIEW THE ROUTE (`review_window`) -> `window`

## mission (clock running)

**AI:** Mission channel ready. The clock is running. Check the calendar and the amber departure instrument beside your seat.

- QUERY TIME AND REPAIRS (`status`) -> `status`
- VIEW WORK SCHEDULE (`tasks`) -> `tasks`
- RECOVER OPTIONAL SUPPLIES (`salvage`) -> `mission`
- REBUILD DEPARTURE ASSEMBLY (`open_repair`) -> `mission`
- READ THE COLONY LOG (`read_story`) -> `story_memory`
- PREPARE TO DEPART (`launch`) -> `launch`

## status (clock running)

**AI:** Reading the live mission instruments. The remaining time and assembly report follow in the system channel. Querying does not stop the clock.

- RETURN TO MISSION (`return_mission`) -> `mission`

## tasks (clock running)

**AI:** 1. RECOVER: salvage optional supplies if you can spare the time. The repair parts are already at the workstation; extra supplies are not required to depart.
2. REBUILD: fit every required assembly part into its socket.

**AI:** 3. CHECK: wait for the assembly to report complete.
4. DEPART: select PREPARE TO DEPART, then confirm while time remains. The complete dated calendar is available; its dates are planning marks, not separate task deadlines.

- OPEN REPAIR WORKSTATION (`open_repair`) -> `mission`
- RETURN TO MISSION (`return_mission`) -> `mission`

## story_memory (story reading)

**System:** PERSONAL LOG / HABITAT 03
If anyone returns, the seed cabinet is under the greenhouse bench. Leave the small lamp on. It helps to think somebody is coming back.

**AI:** There is no departure entry after this recording. I cannot tell you where they went.

**Player:** Keep the log. Someone should remember them.

**AI:** Saved. Your work will resume when you leave this reading window.

- CLOSE LOG / RESUME WORK (`return_mission`) -> `mission`

## repair_ready (clock running)

**AI:** The departure assembly reports complete. That is enough for the low-energy return plan. We can leave as soon as you authorize departure; the window is still counting down.

- PREPARE TO DEPART (`launch`) -> `launch`
- RETURN TO MISSION (`return_mission`) -> `mission`

## launch (clock running)

**AI:** Departure authorization. The assembly must remain complete, and the latest-departure clock must be above zero. Your confirmation is registered at the moment you select it.

**AI:** The route gets us away from Mars. I cannot promise what waits at Earth. Shall we go?

- CONFIRM DEPARTURE (`confirm_launch`) -> `mission`
- KEEP WORKING (`return_mission`) -> `mission`

## ending_home (story reading)

**System:** DEPARTURE ACCEPTED. The countdown has stopped at your authorization time. Ascent to the waiting orbit is cleared.

**AI:** The colony is below us. We reach the waiting orbit. Later, inside the planned return window, the Earth-return burn carries us away from Mars.

**Player:** Is anyone waiting for us?

**AI:** I do not know. I will keep listening.

**System:** You do not know whether home still remembers you. But at last, you are on your way back.

- BEGIN ANOTHER JOURNEY (`restart`) -> `intro`

## ending_stay (story reading)

**System:** WINDOW CLOSED. We can no longer commit to this return plan with the fuel margin available.

**AI:** The next period in our navigation study is 09 NOV 1983 - 19 NOV 1983. We must reassess the craft before using it. Stored food will not last that long. I am sorry.

**Player:** Then tell me what we do tomorrow.

**AI:** The greenhouse still has a sealed seed cabinet. We can bring its water loop back online. I have made a new entry on the calendar.

**System:** 14 SEP 1981: SOW.
Outside, the dust moves across an empty settlement. Tomorrow, you will plant the first seed.

- BEGIN ANOTHER JOURNEY (`restart`) -> `intro`
