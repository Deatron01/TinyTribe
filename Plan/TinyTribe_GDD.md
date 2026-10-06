# TinyTribe: Advanced Multi-Agent Survival Simulator (Game Design Document)

## 1. Project Objectives
The goal is to build a complex 2D grid-based simulation where AI agents, powered by Deep Learning and Reinforcement Learning, learn to survive and build a civilization. Agents must balance individual needs, coordinate with others, and adapt to a dynamic, unforgiving environment. 

## 2. Advanced Game Mechanics

### 2.1 Dynamic Environment & Ecosystem
* **Seasons**: The game features a full seasonal cycle (Spring, Summer, Autumn, Winter), fundamentally changing survival strategies. Winter requires heating and stored food; Summer brings heatwaves and rapid water depletion.
* **Weather Events**: Rain (replenishes water, puts out fires), Storms (destroys weak structures), Heatwaves (doubles thirst and fatigue).
* **Living Ecosystem**: Trees have life cycles (sapling, mature, dead). Over-harvesting leads to deforestation. Resources like stone and iron are finite in local areas, forcing exploration. Wildfires can start from unattended campfires and spread dynamically.

### 2.2 Complex Needs System (The Drives)
Agents have continuous needs that must be managed. If these fail, the agent incurs penalties or dies.
* **Health (HP)**: Decreases from starvation, freezing, or taking damage. Reaches 0 = Death.
* **Hunger**: Drains steadily. Requires varied food sources (berries, cooked meals) to prevent malnutrition.
* **Thirst**: Drains quickly, especially during labor or heatwaves. Requires water from rivers, lakes, or wells.
* **Energy (Fatigue)**: Drains during physical labor (mining, chopping). Restored by sleeping at night.
* **Temperature**: Must be maintained. Freezing in winter requires fires and clothing; overheating requires shade and hydration.
* **Morale / Sanity (New)**: Decreases if isolated, overworked, or living in poor conditions. Low morale reduces efficiency or causes agents to ignore tasks.

### 2.3 Economy, Resources & Crafting
* **Raw Resources**: Pine Wood, Oak Wood, Cobblestone, Marble, Coal, Copper Ore, Iron Ore, Gold Ore, Berries, Wheat Seeds, Wheat, Carrots, Potatoes, Apples, Fish, Water, Herbs, Cotton, Animal Hide, Raw Meat, Bone.
* **Processed Materials**: Planks (Lumber Mill), Stone Bricks (Smeltery), Copper/Iron/Gold Ingots (Smeltery, fuelled by Coal), Fabric (Tailor Loom, from Cotton), Leather (Smokehouse, from Animal Hide), Flour (Windmill, from Wheat), Cured Meat (Smokehouse).
* **Infrastructure Building**: Agents can construct Campfires, Wells, Storage Chests (for shared inventory), workshops, Smelteries, and Cabins (protects from weather). Planks and Stone Bricks are the building materials for most structures beyond the first tents, chests and workshops.
* **Advanced Crafting**: Requires specific workstations.
  * *Axes and Pickaxes*: Four tiers (Wood 1x, Stone 2x, Copper 2.5x, Iron 3x speed), each tier more durable and less tiring. Higher tiers are required for harder nodes (Stone for Oak and ores, Iron for Gold Ore and Marble).
  * *Winter Clothing*: Crafted from 2 Leather + 1 Fabric at the Tailor Loom to survive freezing temperatures.
  * *Healing Potions*: Crafted from herbs and water at a Campfire or Clinic to restore HP.
  * *Bandages / Energy Potions*: Crafted from herbs by a Doctor at the Clinic.
  * *Bread*: Wheat is milled into Flour; 1 Flour + 1 Water is baked at a campfire/stove for high nutrition and a stamina buff.
  * *Bone*: A butchering byproduct used in Bows, Fur Boots and Explorer Backpacks.

### 2.4 Agent Stats, Social Dynamics & Behavioral Traits
When a new agent is spawned (e.g., grows up or wanders into the village), they are generated with randomized Core Stats, Social Stats, and Behavioral Traits. This creates highly distinct AI personalities, forcing the village to manage both material needs and social harmony.

**Core Physical/Mental Stats:**
* **Strength (STR)**: Increases resource gathering speed (wood, stone) and carry weight.
* **Dexterity (DEX)**: Increases crafting speed, building speed, and tool durability.
* **Intelligence (INT)**: Improves healing efficiency, farming yield, and complex recipe crafting.
* **Endurance (END)**: Reduces fatigue drain and increases resistance to extreme temperatures.

**Social Stats (Determines AI decision-making & MARL reward weights):**
* **Influence / Charisma**: Determines the agent's voting power in the Council. High influence agents can convince others to change roles or share resources.
* **Empathy**: Modifies the agent's internal reward function. High empathy agents heavily prioritize "Global Rewards" (village survival) and will freely share food. Low empathy agents prioritize "Individual Rewards" and may hoard resources.
* **Sociability / Extroversion**: Determines how the Morale need drains. Extroverts lose morale quickly when alone but gain it fast in groups. Introverts are unaffected by isolation.
* **Aggression**: Likelihood of fighting back if attacked, or taking resources from others by force if starving.

**Behavioral Traits (The Intellect Threshold):**
In the early game (The Stone Age), agents are driven purely by survival instinct. They do not have complex personalities. However, as generations pass and agents reproduce, their base **Intelligence (INT)** stat naturally increases. Once a newborn's INT crosses a high threshold (usually around the Iron/Gold Age), they achieve higher consciousness and "unlock" 1-2 unique behavioral traits that modify their AI logic:
* *Glutton*: Hunger drains 20% faster, but gets a massive Morale boost from eating cooked meals.
* *Loner*: Morale doesn't drain when isolated, but suffers a 50% penalty to Council Influence.
* *Workaholic*: Energy drains slower when performing their assigned role, but Morale drops if idle.
* *Lazy*: Energy drains faster during work, but recovers much faster when sleeping.
* *Altruist*: Will actively drop personal resources (food/medicine) near agents with critically low stats.
* *Kleptomaniac*: Occasionally takes items from the shared storage chest into personal inventory without immediate need.
* *Natural Leader*: Other agents get a Morale and efficiency buff when working near this agent.

### 2.5 Roles & The Council Voting System
Instead of hard-coded roles, agents are assigned jobs dynamically through a **Village Council Voting System**.
When a new agent arrives, the existing AI agents evaluate both the new agent's statline and the village's current material deficit (e.g., "We are low on wood" or "We need a healer"). The agents cast a vote, and the role with the majority consensus is assigned.
* **Lumberjack** (Relies on STR, END): Gathers raw wood and operates the *Lumber Mill* to create planks.
* **Miner** (Relies on STR, END): Extracts stone and ores from caves/mountains.
* **Farmer** (Relies on INT, END): Manages agricultural plots and operates the *Windmill* to mill flour.
* **Cook** (Relies on INT): Bakes bread and crafts high-nutrition meals at the *Cooking Stove*.
* **Butcher** (Relies on STR, DEX): Processes hunted animal carcasses at the *Smokehouse* for meat, leather, and bone.
* **Tailor** (Relies on DEX): Operates the *Loom* to sew Winter Clothing and inventory backpacks.
* **Blacksmith** (Relies on DEX, STR): Smelts ores and crafts advanced tools at the *Anvil*.
* **Doctor** (Relies on INT, DEX): Crafts medicine at the *Clinic* and cures sicknesses.
* **Builder** (Relies on DEX, END): Constructs infrastructure, requiring a *Drafting Table* for late-game blueprints.


### 2.6 Multi-Tiered Inventory & Logistics System
Managing resources is a core challenge for the AI, functioning across multiple layers of access permissions:
* **Personal Inventory (Own)**: Highly limited capacity (3-5 slots) governed by the agent's Strength (STR). Holds actively equipped tools, immediate meals, and freshly gathered materials before drop-off.
* **House Inventory (Private)**: Storage chests built inside a constructed home. Medium capacity (10-15 slots). *Only accessible by the specific agent(s) who live in that house.* This creates fascinating AI behavior: Low-Empathy agents will hoard rare food and winter clothes in their private house chest, letting the rest of the village starve.
* **Town Inventory (Shared/Public)**: Large, publicly accessible structures built in the center of the village. High-Empathy agents naturally deposit gathered goods here.
  * *General Storage Chest*: Holds raw wood, stone, and iron for Builders.
  * *Town Granary*: Specialized building that holds mass amounts of food (wheat, meat, berries) and significantly slows down food rot.
  * *Tool Rack / Armory*: Where the Blacksmith deposits finished axes and spears for other agents to take.

### 2.6.5 Architecture & The Prefab Building System
The game does not use a block-by-block building system (like Minecraft). Instead, it uses a **Prefab Blueprint System** (like Age of Empires or RimWorld). Builders gather specific raw materials and place down complete structures.

**Building Materials:**
* *Pine Wood*: Basic, fast to gather. Used for early game.
* *Oak Wood*: Hardwood, slow to gather. Required for durable, late-game structures.
* *Planks*: Sawn from Pine Wood at the Lumber Mill (1 Pine Wood = 2 Planks). Used for the Pine Cabin, Granary, Lumberyard, Clinic, Library, Farm Plot and Tool Rack.
* *Cobblestone*: Basic rock for foundations and furnaces.
* *Stone Bricks*: Fired from Cobblestone at the Smeltery. Used for the Stone Manor, Prison, Council Square and Village Bank.
* *Marble*: Rare stone mined deep underground, used for Gold Age community buildings.
* *Iron / Gold Ingots*: Smelted from ore.

**Structure Categories:**
* **Residential**: 
  * *Hide Tent*: Cheap, fits 1-2 agents, prevents freezing but vulnerable to storms.
  * *Pine Cabin*: Fits 2-4 agents, good weather protection.
  * *Oak Lodge*: Fits large families, high durability against storms.
  * *Stone Manor*: Extremely expensive, immune to storms, massive Morale boost for inhabitants.
* **Storage / Logistics**: Town Granary (Food), Lumberyard (Wood), Ore Silo (Stone/Metals), Tool Rack / Armory (Tools/Weapons).
* **Production Workshops**: Every craft has a dedicated workshop the Builder must construct:
  * *Lumber Mill* (Lumberjack)
  * *Farm Plot & Windmill* (Farmer)
  * *Cooking Stove* (Cook)
  * *Smokehouse / Butcher Block* (Butcher)
  * *Tailor Loom* (Tailor)
  * *Smeltery & Anvil* (Blacksmith)
  * *Clinic* (Doctor)
  * *Drafting Table* (Builder)
* **Utility**: Stone Well (infinite water), Campfire.
* **Community**: Clinic, Library / Lab, Prison. Late game: Council Square (boosts voting morale), The Market (trade hub), Village Bank (holds the village Gold).
* **Home Ownership**: When agents claim a residential building, they gain access to its House Inventory. Sleeping in their own house provides a 'Sheltered' buff, speeding up Energy recovery and preventing Temperature drain.

### 2.7 Wildlife, Hunting & Sicknesses
* **Hunting**: The ecosystem is populated by animals (Rabbits, Deer, Wolves, Bears). Agents must craft weapons (Spears, Bows) to hunt. Raw meat drops must be cooked at a campfire; eating it raw causes Food Poisoning.
* **Sicknesses & Diseases**: Agents can contract diseases that severely impact performance, making the 'Doctor' role critical:
  * *Common Cold*: Caught from freezing or rain. Drains energy 2x faster and lowers max HP.
  * *Food Poisoning*: Caught from raw meat or bad berries. Causes vomiting (massive energy loss) and rapid hunger.
  * *Infection*: Caught from Wolf bites. Continually drains HP until a Doctor applies a crafted Bandage.
  * *Bleeding*: Caused by Bear attacks. Fast HP drain for a short time; stopped by a Bandage.

### 2.8 Social Interactions & The Emote System
Agents **cannot talk**. Instead, they communicate and satisfy their Morale need via a visual Emote System (bubbles above their heads).
* When an agent outputs a 'message vector' (as described in 3.2), the UI renders it as an Emote.
* **Emotes include**: [Food?] (starving), [Exclamation] (danger/wolf spotted), [Resource] (found iron/wood), [Angry] (someone stole food), [Happy/Heart] (socializing or receiving an item).
* When agents face each other and 'flash' emotes, they are conversing. High Sociability agents gain a massive Morale boost from these interactions.
* The AI can deliberately send seven emotes (Food?, Danger, Resource, Happy, Angry, Trade?, Follow Me). The other five (Medical, Freezing, Overheating, Exhausted, Broken Tool) are shown automatically by the engine when their trigger condition is met.

### 2.9 Skill Levels & Experience Progression
As agents perform actions, they gain Experience Points (XP) and level up specific skills.
* **Skills**: Woodcutting, Mining, Farming, Cooking, Hunting, Building, and Medical.
* **Leveling Up**: Reaching Level 2 in Woodcutting, for example, speeds up chopping animations and halves the Energy cost.
* Agents with high Intelligence (INT) earn XP faster. This encourages the Village Council to keep agents in the roles they have leveled up in, rather than constantly reassigning them.


### 2.10 The Social Web & Relationship Matrix
Every agent tracks a 'Relationship Score' (-100 to +100) with every other agent in the village, forming a complex social graph.
* **Best Friends (>50)**: Agents will share their private House Inventory, sleep in the same house, prioritize healing each other, and vote together in the Council.
* **Rivals (<0)**: Agents will refuse to heal each other, vote against each other's proposals, and may even steal from each other if the Aggression stat is high.
* **UI Visualization**: The game interface features a 'Social Web' view, displaying nodes (agents) and colored lines (relationships) so the human observer can watch alliances and rivalries form in real-time.

### 2.11 Ages & Achievement System (Macro-Progression)
To ensure the AI always has a challenge, the simulation progresses through historical 'Ages' triggered by Village Achievements. Each Age shifts the AI's loss function and environment parameters.
* **The Stone Age**: 
  * *Focus*: Raw survival, gathering wood/stone, fighting off wolves.
  * *Transition Achievement*: 'Discover Mining' (Survive the 1st Winter, mine 50 Iron Ore, and research Metallurgy).
* **The Iron Age**: 
  * *Focus*: Infrastructure, agriculture, and handling complex logistics/sicknesses.
  * *Transition Achievement*: 'Establish an Economy' (Build a Granary and a Clinic, mint 100 Gold Coins, and research Economics).
* **The Gold Age**: 
  * *Focus*: Wealth accumulation, trade, and social stability.
  * *Endgame Achievement*: 'Utopia' (Hold 1000 Gold in the Village Bank with 0 starving agents).

### 2.12 The Economy & Currency (Late Game Challenge)
When the village enters the **Gold Age**, survival against the elements becomes trivial due to advanced infrastructure. To introduce a new challenge, the simulation enforces a **Currency System**.
* **Gold Coins**: Gold Ore is smelted into Gold Ingots; the Blacksmith mints 10 coins per ingot (available from the Iron Age).
* **Capitalism Transition**: The AI's reward function shifts. Agents will no longer deposit resources into the Town Storage for free. They demand a 'Wage' in Gold.
* **The Marketplace**: Agents must use their earned Gold to buy food from the Cook or tools from the Blacksmith. 
* **Wealth Inequality**: If a few agents hoard all the Gold, the impoverished agents will suffer massive Morale penalties. This tests if the MARL (Multi-Agent RL) system can invent taxation, charity, or if it collapses into a social revolt.


### 2.13 Lifespan, Marriage & Generations (Neuroevolution)
To make the simulation truly self-sustaining, agents are not immortal. They go through a full life cycle, which transforms the project into a blend of Reinforcement Learning and Evolutionary Algorithms.
* **Ageing Phases**:
  * *Childhood*: Cannot do hard labor (mining/chopping). However, they gain XP 3x faster by observing adults working.
  * *Adulthood*: Peak physical stats. Capable of all roles and marriage.
  * *Elderly*: Physical stats (STR, END) decay rapidly, but INT and Influence increase. Elders naturally transition out of labor roles and into Doctor or Council Leader roles.
  * *Death*: Inevitable death by old age forces the village to constantly train the next generation.
* **Marriage & Grief**: If two Adult agents maintain a 'Best Friend' status (+80) for a set time, they can Marry. They permanently share a House and its inventory, and receive a constant Morale buff. However, if one dies, the survivor experiences 'Grief' (massive Morale drain).
* **Reproduction & Genetics (Neuroevolution)**: Married couples living in a sheltered House can spawn a Child agent. The child's core stats, traits, and crucially, its **Neural Network Weights**, are a crossover/mutation of the two parents. This means natural selection occurs: agents with AI models that are bad at surviving die out, while successful agents pass on their superior neural networks to the next generation.


### 2.14 The Justice System & Crime
With distinct AI personalities (Kleptomaniacs, Low Empathy hoarders, Aggressive agents), internal conflict is inevitable.
* **Crimes**: 
  * *Theft*: Taking from the Town Granary when village food is critically low.
  * *Assault*: A high-aggression agent attacking a rival.
  * *Hoarding*: An agent storing >50% of the village's wealth in their private House Inventory.
* **The Law**: The Council can vote to convict an agent. If convicted, the new **Guard / Sheriff** role will arrest the agent.
* **Punishments**:
  * *Fines / Seizure*: The Guard forcibly moves items from the criminal's House Inventory back to the Town Storage.
  * *Jail*: The agent is locked in a constructed **Prison** prefab for a set time (heavy Morale penalty, no productivity).
  * *Exile*: The ultimate punishment. The agent is permanently banished to the wilderness.

### 2.15 The Technology Tree & Research
The village does not start with all blueprints unlocked. Progression requires dedicated scientific effort.
* **The Scholar**: A new role relying entirely on Intelligence (INT). Scholars do not gather food or build; they sit in the **Library / Laboratory** generating 'Research Points' (RP).
* **Tech Nodes**: The Council votes on what to research next. 
  * *Agriculture (100 RP)*: Unlocks Farm Plot & Windmill, Farming Hoe, Flour and Bread.
  * *Medicine (200 RP)*: Unlocks Clinic, Bandage and Energy Potion.
  * *Metallurgy (300 RP)*: Required for the Iron Age (Unlocks Smeltery, Anvil, Cooking Stove, Copper/Iron Tools, Stone Bricks, Gold Minting, Prison, Stone Manor).
  * *Economics (1000 RP)*: Required for the Gold Age (Unlocks Market, Village Bank, Council Square).
  * An Age begins only when both its technology is researched and its Transition Achievement (2.11) is met.
* *Simulation Challenge*: The village must sacrifice food and labor to keep the Scholar alive while they invent, balancing short-term survival with long-term technological gain.

### 2.16 Ideologies & Cultural Beliefs (Memes)
Neural networks can invent and spread abstract concepts via the Emote communication channel.
* **Viral Ideas**: Influential agents (High Influence stat) can randomly generate an 'Ideology' based on their environment, which spreads to their friends.
* **Examples**:
  * *Nature Worship*: Imposes a massive Morale penalty if Oak trees are chopped down, forcing the village to rely heavily on stone/mining.
  * *Cult of Industry*: Miners and Blacksmiths are revered, receiving double voting weight in the Council and massive Morale buffs when smelting.
  * *Communalism*: Private House Inventories are outlawed; all items must be stored publicly.

### 2.17 Exploration & The Fog of War
The map is massive, but initially shrouded in darkness (Fog of War).
* **The Scout**: A new role (Relies on Endurance and Dexterity). Scouts consume 50% less food while traveling.
* **Expeditions**: Rare resources like Gold Ore and Marble do not spawn near the starting village. The Scout must craft an **Explorer Backpack** and **Travel Rations** to survive long treks into the darkness to map out resource veins and expand the village's territory.


### 2.18 Horizontal Depth & Itemization (Scaling)
To increase the complexity of the Reinforcement Learning state-space, the simulation features massive horizontal depth in its core systems:
* **Tool Tiers**: Wood -> Stone -> Copper -> Iron. Specific tools are needed for specific tasks (e.g., *Farming Hoe*, *Builder Hammer*, *Fishing Rod*).
* **Clothing Slots**: Agents can equip different clothing for specific weather (*Fur Boots* for snow movement, *Leather Hats* for heatwaves).
* **Dietary Needs**: Agents cannot survive on meat alone. A lack of varied food (Apples, Carrots) causes *Scurvy*.
* **Mental Health (Depression)**: If Morale hits 0 for too long, the agent contracts *Depression* and will refuse to work, requiring social intervention from other agents.
* **Expanded Traits**: The gene pool includes traits like *Coward*, *Brave*, *Night Owl*, *Clumsy*, and *Green Thumb*, ensuring that no two agents behave identically.
* **Expanded Emotes**: Agents can signal specific needs: [Freezing], [Exhausted], [Broken Tool], [Follow Me], and [Trade?].

### 2.19 Simulation Units & Data Tables
All concrete numbers (drain rates, recipes, costs, durations, probabilities) live in the `Sim ...` sheets of `AISim_English_v12.xlsx`, which the engine loads directly. Where this document and a `Sim` sheet disagree, the `Sim` sheet wins.
* **Time**: 1 tick = 1 agent action. 240 ticks = 1 day (160 day, 80 night). 7 days per season, 28 days per year. Agents age 1 year every 4 days, so a 60-year life lasts 240 days.
* **Scales**: Needs run 0-100. Core stats (STR, DEX, INT, END) run 1-10. Social stats (Influence, Empathy, Sociability, Aggression) run 0-1.
* **Phases**: Every item, recipe, building and resource node is tagged phase 1 (Stone Age), 2 (needs Metallurgy) or 3 (needs Economics). The first prototype implements phase 1 only.
* **Status**: The current values are a first pass chosen to be internally consistent; they have not been playtested.

## 3. Deep Learning & AI Architecture

### 3.1 Data Collection & Training Pipeline
* **Phase 1: Behavioral Cloning (BC)**: Human players control agents to generate initial datasets (State -> Action mappings). This jumpstarts the AI's understanding of basic survival mechanics.
* **Phase 2: Multi-Agent Reinforcement Learning (MARL)**: Agents transition to autonomous learning via PPO (Proximal Policy Optimization) with Centralized Training and Decentralized Execution (CTDE).

### 3.2 State Representation (How the AI Sees)
The AI does not process raw pixels. Every tick, the environment passes a multidimensional tensor representing the agent's observable state.
* **Local FOV Tensor (e.g., 11x11x5 Grid)**:
  * *Channel 1 (Terrain)*: Integers mapping to Walkable, Water, Mountain, etc.
  * *Channel 2 (Entities)*: Locations of other Agents, Wolves, Deer.
  * *Channel 3 (Resources)*: Locations of Trees, Ores, Berries.
  * *Channel 4 (Structures)*: Locations of Houses, Granaries, Workshops.
  * *Channel 5 (Social/Emotes)*: Incoming discrete emote vectors from nearby agents.
* **Internal State Vector (1D Array)**:
  * Normalized floats (0.0 to 1.0) representing: [HP, Hunger, Thirst, Energy, Temp, Morale].
  * Agent's genetic stats: [STR, DEX, INT, END, Influence, Empathy, Sociability, Aggression].
  * Personal Inventory contents: 10 slots, each as (item id, quantity), plus the equipped tool, its durability and the clothing slots.
  * Context: time of day (sin/cos), season, weather, role and age. Without these the agent cannot tell night from day or winter from summer.
  * Self status: sickness flags, skill levels, traits, ideology, married / jailed / convicted / has-home flags, and the direction to its home and to the village centre.
  * Faced target: progress on the tile in front; if it is an agent, that agent's HP (exact only for a Doctor), relationship score, role, and whether it is the spouse or a convict.
  * Village status: stock levels, population, current Age, research progress, share of starving agents and the number of agents per role. This is what lets an agent vote sensibly.
  * Vote context: which Council vote is open and the stats of the agent being voted on.
  * Total length: 132 floats. The exact index layout is in the `Sim Observation` sheet.
* **Action Masks**: Every tick the engine also provides a mask of valid actions and valid arguments (e.g. Craft is masked without the ingredients, Vote is masked when no vote is open). Masks are applied to the policy's outputs, so the agent never has to learn which actions are impossible.
* **Memory (LSTM/GRU)**: The combined flattened outputs of the CNN (processing the Grid) and the MLP (processing the Vector) are passed through an LSTM hidden state, allowing the agent to remember where it saw a resource off-screen 50 ticks ago.

### 3.3 The Action Space (Outputs)
Every tick, the neural network outputs a probability distribution over a **Discrete Action Space**. The agent executes the action with the highest probability (or samples during exploration).
* **Movement (4)**: [Move North, Move South, Move East, Move West]
* **Interaction (3)**: [Harvest/Attack Node in front], [Deposit to Storage], [Take from Storage]
* **Crafting/Building (1)**: [Execute Blueprint/Recipe at Workshop] (The specific recipe is determined by a secondary categorical output if this action is chosen).
* **Communication (5)**: [Emote: Food?], [Emote: Danger!], [Emote: Resource], [Emote: Social/Happy], [Emote: None]
* **Communication, extended (3)**: [Emote: Angry], [Emote: Trade?], [Emote: Follow Me]
* **Self (2)**: [Consume Item] (eat, drink or use medicine; the agent drinks directly when facing water), [Sleep / Rest] (restores Energy, faster at night and inside the agent's own house; sleeping in an unowned house claims it).
* **Inventory (2)**: [Equip / Unequip] (chooses the tool, weapon or clothing in use), [Give / Use On] (hands an item to the faced agent; medicine is applied to them, which is how a Doctor heals).
* **Council (1)**: [Vote] (casts an Influence-weighted vote on a role assignment, the next technology, or a conviction).

That is 21 discrete actions in total. A second categorical output, the **argument head** (66 values), selects the item for Deposit / Take / Consume / Equip / Give, the entry for Craft/Build (any recipe, any building, a crop to plant, or research at the Library), and the option for Vote.

The same few actions reach every mechanic through context: Harvest/Attack on water fishes when a rod is equipped, on a convict it detains when used by a Guard; Deposit and Take at the Market sell and buy for Gold; Deposit at a campfire adds fuel. The full mapping of mechanics to actions is in the `Sim Capability Audit` sheet and the exact resolution rules are in `Sim Rules`. Marriage, reproduction, ageing, sickness, ideology spread and relationship scores are resolved by the engine from behaviour and are not actions.

### 3.4 The Reward Function (Mathematical Balancing)
The loss function is dynamically modified by the agent's genetic **Empathy** stat (ranging from 0.0 to 1.0). This mathematically forces different social behaviors in MARL.

* **Equation Base**: 
  R_total = (R_individual * (1.0 - (Empathy / 2))) + (R_global * Empathy)

* **Individual Reward (R_individual)**:
  * +1.0 for consuming food/water when stat is < 50%.
  * +0.5 for sleeping when exhausted.
  * -2.0 for taking damage (wolves, freezing).
  * -0.1 per tick if any stat is at 0%.
  * Work: +0.05 per item gathered, +0.2 per recipe crafted, +0.2 per crop planted, +0.3 for equipping a better tool tier or a new clothing slot (once each), +0.2 for refuelling a dying campfire at night or in winter.
  * Self-care: +1.0 for curing its own sickness, +1.0 for claiming a home (once).
  * Exploration and research: +0.01 per newly revealed map tile (doubled for Scouts), +0.02 per Research Point generated.
  * Social: +0.1 for casting a Council vote, +0.1 for a positive emote exchange, +0.1 for a useful emote (Danger with a predator in view, Resource at a node, Follow Me that is followed), +2.0 on marriage.
  * Economy (Gold Age): +0.2 per Market trade, +0.02 per Gold Coin earned.
  * Crime: -2.0 when convicted, -0.05 per tick in jail.

* **Global/Village Reward (R_global)**:
  * +5.0 when a new structure is completed by *any* builder.
  * +0.5 every time the Town Granary net food count increases.
  * -5.0 if any agent in the village dies (other than from old age).
  * Village milestones (all agents): +10.0 when a technology is researched, +20.0 when the village enters a new Age, +10.0 for a winter with no deaths, +3.0 when a child is born, +0.5 for the first sighting of a Gold Vein or Marble Deposit, +100.0 for Utopia, -1.0 when a Theft or Assault is witnessed.
  * Pro-social acts (credited to the acting agent, but still scaled by its Empathy): +0.2 per stack deposited in Town storage, +1.0 when a gifted item is consumed by a recipient in need, +2.0 for healing another agent, +1.0 for killing a Wolf or Bear near the village, +2.0 for a Guard detaining a convict, +0.5 per stack a Guard returns to Town storage.

Repeatable rewards carry caps or conditions so they cannot be farmed (e.g. a gift only pays out when the recipient actually consumes it while in need; votes pay once per vote; emotes have a cooldown). The full table, with the limit on each reward, is the `Sim Rewards` sheet; `Sim Capability Audit` shows which reward covers each ability.

*Behavioral Emergence*: A Low Empathy agent (Empathy = 0.1) receives almost no reward for depositing food in the Granary, so it learns to hoard it in its House Inventory to guarantee its R_individual. A High Empathy agent (Empathy = 0.9) receives massive rewards when the village succeeds, naturally adopting the Altruist trait.


### 3.5 Tech Stack & Cross-Platform Engine
To ensure the simulation can be played and observed on both **Desktop PCs and Mobile Phones**, we must choose a highly optimized cross-platform engine. The engine must also support Python-based Deep Learning frameworks (PyTorch) for the training phase.

**Option A: Unity (Recommended for AI Integration)**
* *Why*: Unity has a first-party, enterprise-grade machine learning toolkit called **Unity ML-Agents**. This allows us to define the agents, the grid, and the rewards directly in C#, while Unity automatically bridges the environment to a Python PyTorch training loop in the background.
* *Pros*: Out-of-the-box reinforcement learning support, highly optimized for mobile (Android/iOS) and PC, massive asset store for 2D sprites.
* *Cons*: Heavier engine footprint.

**Option B: Godot Engine (Recommended for Open-Source/2D)**
* *Why*: Godot is an incredibly lightweight, open-source engine that excels at 2D grid-based games. 
* *Pros*: Completely free, boots instantly, very easy to export to Android, iOS, and PC. Uses GDScript (similar to Python).
* *Cons*: No official first-party ML toolkit. We would have to use community wrappers (like *Godot RL Agents*) to connect the game engine to our PyTorch training loop.

**The Tech Stack Pipeline:**
1. **Training Environment**: The engine runs in 'headless mode' on a powerful desktop or cloud server at 100x speed. Python (Ray RLlib / PyTorch) handles the neural network weight updates.
2. **Inference (Gameplay)**: Once the AI models are trained (e.g., reaching the Gold Age successfully), the model weights are exported to .onnx (Open Neural Network Exchange) format.
3. **Cross-Platform App**: The .onnx models are embedded directly into the Unity or Godot game client. When a player opens the app on their phone, the game runs locally, querying the pre-trained neural network for agent decisions without needing a server connection.

## 4. Evaluation & Performance Metrics
* **AI Metrics**: Loss curves, action prediction accuracy on the Validation Set (imitating humans).
* **Simulation Metrics**: Average colony lifespan (in ticks), resource generation rate, percentage of successful winters survived, tool usage efficiency, and successful crafting events.
* **Ablation Studies**: Comparing a purely reactive AI (Simple MLP) vs. Memory AI (CNN+LSTM) vs. Communicating Multi-Agent AI.

## 5. University Course Alignment (Roadmap)
This project is explicitly structured to fulfill the requirements of the **Introduction to Machine Learning Lab**.

### Phase 1: Data Collection & Processing (Weeks 1-5)
* **Week 2 (Project Idea Submission)**: Submit the TinyTribe GDD.
* **Data Collection**: Before the AI can learn, we build a basic prototype and play it manually. The engine logs every tick: the Environment State (the 11x11x5 Grid + the 132-float state vector + the action masks) and the Human Action (one of the 21 actions plus its argument), in the format defined by the `Sim BC Log Schema` sheet. This creates a massive supervised dataset.
* **Data Processing**: We normalize internal stats (HP/Hunger) to 0.0-1.0. We use One-Hot Encoding or Embedding for the discrete grid tiles (Pine Wood vs Oak Wood).
* **Week 5 (Dataset Presentation)**: We present the cleaned, formatted CSV/JSON dataset of human gameplay.

### Phase 2: Modeling & Architecture (Weeks 6-9)
We will implement the exact architectures covered in the course using PyTorch/TensorFlow:
* **Week 6 (Autoencoders)**: Optionally use Autoencoders to compress the 11x11x5 Grid into a smaller latent vector to speed up training.
* **Week 7 (CNNs)**: Implement Convolutional layers to process the agent's spatial vision.
* **Week 8 (RNNs/LSTMs)**: Implement Recurrent layers to process time-series data, giving the agent memory of past actions.
* **Week 9 (Initial Models & Custom Loss)**: Present the first Behavioral Cloning models. We will write a **Custom Loss Function** (as detailed in Section 3.4) that integrates the Empathy stat to penalize starvation and reward village stockpiling.

### Phase 3: Packaging & App Implementation (Weeks 10-14)
* **Week 10-12 (App Implementation)**: The trained models are packaged into .onnx or TensorFlow Lite format. They are embedded into the Unity/Godot engine and exported as a standalone **Mobile App** (Android) or Web App. 
* **Week 13 (Docker)**: We will Dockerize a backend server to track a global leaderboard of which user's AI village survived the longest.
* **Week 14 (Final Presentation)**: Live demonstration of the app running on a mobile device, showing the fully autonomous AI village surviving the Winter.
