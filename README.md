# GenericEventSystem

## Intro

This is a messaging pattern event system, slightly improved with a few quality of life thingies.
If you are not familiar with a messaging design pattern, you can learn core concepts in [this oldie tutorial by Unity](https://learn.unity.com/tutorial/create-a-simple-messaging-system-with-events#). It's also worth to take a look at a great classic book on coding patterns "Design Patterns. Elements of Reusable Object-Oriented Software", by E.Gamma, R.Helm, R.Johnson, J.Vlissides. A way smaller read on [Wiki](https://en.wikipedia.org/wiki/Messaging_pattern). Complete list of [design patterns Wiki page](https://en.wikipedia.org/wiki/Software_design_pattern).

## Installing

### Install: Manual
- Git clone or fork this repo into your Unity project /Assets/Scripts/GenericEventSystem to add all messaging pattern implementation scripts.
- Then add this folder to .gitignore to be able to separately pull on updates / fixes.

### Install: Managed
To import the package from GitHub, add it as a dependency in your Unity project’s Packages/manifest.json.

1. Add Git URL to manifest.json: Open Packages/manifest.json in the target Unity project, and add your package under dependencies. For example:

```
{
  "dependencies": {
    "com.DarthCarrotPie.GenericEventSystem": "https://github.com/Darth-Carrotpie/GenericEventSystem.git#v1.0.2"
  }
}
```
2. Verify Installation: Unity should recognize the package and download it directly from the repository, making it available in the project’s Package Manager.

## Usage

The there are two big differences from the Unity tutorial example:

- Message
- EventName

A few examples on listening and triggering events events can be found in ./Examples folder. If you are familiar with the concepts, I suggest to jump right in, open the example scene and test them out.

### Message

Message object is what you will pass to your event. You will be placing important info there, i.e. position vector, delta float, size int and even custom objects. That will require fitting the `GameMessage` object to your purposes. It inherits from `BaseMessage`, which contains mainly things for debugging and convenience methods, so no need to touch it. This is how you extend the `GameMessage`, you will need to add 4 lines of code:

```
private Transform _transform;
private bool transformSet;
public Transform transform { get { return base.GetItem(ref _transform, transformSet); } }
public GameMessage WithTransform(Transform value) => base.WithItem<Transform>(ref _transform, value, ref transformSet);
```

The bool \*Set is used via reflection for debugging purposes so naming has to be exact as it's variable name +"Set" added at the end. See more about debug logic within BaseMessage.ToString() method. For this purpose you must not extend `GameMessage` with a bool variable, instead use int and convert to bool within your Listeners.

Within the Trigger you will need to create and write the Message object like so:
`GameMessage.Write().WithTransform(transform).WithIntMessage(7))`
Here you are writing and setting the variables of type `Transform` and `int` into the new `GameMessage`.

### EventName

`EventName` object is a static bucket type to add convenience to write new events, figure out what events do what via using OOP principles. In the end they are `string` types as in the Unity tutorial, though arranged in a more intuitive way.

To extend the EventName object, you will need to add a string variable, like so:
`public static string MyNewEventNameValue() { return MyNewClass_MyNewEventNameValue"; }`
under a class which you chose. Then also add a return value for newly created Method:
`public static List<string> Get() { return new List<string> { MyNewEventNameValue(), }; }`
This ensures exports to Lists, when needed, i.e. within Editor. You can as well extend the classes, to create names fitting for your project. This is how it could look complete:

```
public class MyNewClass {
    public static string None() { return null; }
    public static string MyNewEventNameValue() { return MyNewClass_MyNewEventNameValue"; }
    public static List<string> Get() { return new List<string> {None(), MyNewEventNameValue(), }; }
}
```

I suggest always adding a None() to have a fallback for your game designers/testers.

### Triggering and Listening

Pretty much same as in tutorial, except here you will have to input the GameMessage and all the Listeners.
Triggering an event with a message:

```
string scoreMessageString = "You Win!";
void Update() {
    if (Input.GetKeyDown(KeyCode.Space)) {
        EventCoordinator.TriggerEvent(EventName.UI.ShowScoreScreen(), GameMessage.Write().WithStringMessage(scoreMessageString).WithTransform(transform).WithIntMessage(42));
    }
}

```

Any amount of Listeners can be listening to the event and expecting the message:

```
void Start() {
    EventCoordinator.StartListening(EventName.UI.ShowScoreScreen(), OnScoreShowReceived);
}

void OnScoreShowReceived(GameMessage msg) {
    Debug.Log(msg.transform);
    Debug.Log(msg.strMessage);
}
```

Don't forget to add `using GenericEventSystem;` to include the namespace.

TIP: if unsure what triggers/listens to the event, just select the whole event name chain (in this case - "EventName.UI.ShowScoreScreen()") and do a project-wide search (ctrl+shift+F in VS Code) to find all triggers throughout the project.

### Full Example with a CustomObject

Let's create a custom custom object to test sending the message.

```
public class CustomObject {
    public string value;
    public CustomObject(string newVal) {
        value = newVal;
    }
}
```

For added complexity, let's instead send a `List<CustomObject>`, not a single object:

```
//Send a list of custom objects
if (Input.GetKeyDown(KeyCode.L)) {
    string scoreMessageString = "Custom object value.....!...!";
    CustomObject newObj1 = new CustomObject(scoreMessageString + "  1!");
    CustomObject newObj2 = new CustomObject(scoreMessageString + "    2!");
    List<CustomObject> list = new List<CustomObject>();
    list.Add(newObj1);
    list.Add(newObj2);
    EventCoordinator.TriggerEvent(EventName.Input.Menus.ShowSettings(), GameMessage.Write().WithTargetCustomObjects(list));
}
```

Before we can Trigger this event, however, we need to extend the `GameMessage` and `EventName` accordingly. First `GameMessage`:

```
private List<CustomObject> _targetCustomObjects;
private bool targetCustomObjectsSet;
public List<CustomObject> targetCustomObjects { get { return base.GetItem(ref _targetCustomObjects, targetCustomObjectsSet); } }
public GameMessage WithTargetCustomObjects(List<CustomObject> value) => base.WithItem<List<CustomObject>>(ref _targetCustomObjects, value, ref targetCustomObjectsSet);

```

and `EventName` with new events `Input.Menus.ShowSettings()` and `Input.PlayerReady()`. Notice how they are of different depth/hierarchy:

```
public class Input {
    public class Menus {
        public static string ShowSettings() { return "Input_Menus_ShowSettings"; }
        public static string None() { return null; }
        public static List<string> Get() { return new List<string> { ShowSettings(), None() }; }
    }
    public static string PlayersReady() { return "Input_PlayersReady"; }
    //nesting can be done indefinitely but Get() function must get it's depth as well as follows:
    public static List<string> Get() {
        return new List<string> {
                PlayersReady(),
            }.Concat(Menus.Get())
            .Concat(Network.Get())
            .ToList();
    }
}
```

However, too deep of a hierarchy is not advised, because it can get confusing. I suggest to try keep it under 4 levels.

We then Listen to the new event and new GameMessage property like so. For complexity sake, let's listen to two events in one script:

```
void Start() {
    EventCoordinator.StartListening(EventName.Input.Menus.ShowSettings(), OnShowSettings);
    EventCoordinator.StartListening(EventName.UI.ScoreScreenShown(), OnScoreScreenShown);
}
void OnShowSettings(GameMessage msg) {
    foreach (CustomObject ob in msg.targetCustomObjects) {
        Debug.Log(ob.value);
    }
}
void OnScoreScreenShown(GameMessage msg) {
    Debug.Log(msg.strMessage);
}
```

That's it. If all done correctly, you should get the message in your console window.

## Knowledge base

- Enable debugging on EventCoordinator component to see all listened events;
- If the event is triggered, but not listened, it will not show in logs;
- Only messages fields which are set via `\.With\*` type method will be displayed in console debug;
- You can set debug mask which events to print into console, when debugging is enabled. Use this to prevent events like 'ticks' spamming the console; Only filter what you are working with;
- See EventChain script comments on how to attach events.
- Attaching can be done only once. Intended usage is to register completion of a normal event.
- If an event errors out within the chain, it throws exceptions and the chain is not completed. However following event triggering will continue to run as intended.
- If unsure what triggers/listens to the event, just select the whole event name chain (i.e. "EventName.UI.ShowScoreScreen") and do a project-wide search (ctrl+shift+F in VS Code) to find all triggers throughout the project.

## Extras

- Singleton Class

You can inherit from this class to create singleton pattern instances of the script. This is what EventCoordinator extends on.

- Extension methods

A few Unity object, math and other extension methods that I find often using. These could be expanded and turned into a separate repo in the future, but for now I'm keeping them here.

## Comment, Like and Subscribe xD
