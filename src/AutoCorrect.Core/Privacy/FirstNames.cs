namespace AutoCorrect.Core.Privacy;

/// <summary>
/// First names used to recognise a person in a text.
///
/// Why a list and not a rule: in German every noun is capitalised, so "capitalised word" says
/// nothing at all. A known first name is a strong signal, and the capitalised word right after
/// it is then almost always the surname. That keeps the detection precise instead of masking
/// half the sentence.
///
/// The list is deliberately common names only. It is not complete and cannot be - see the
/// documentation of <see cref="PrivacyMask"/> for what that means.
/// </summary>
internal static class FirstNames
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // German speaking, male
        "Andreas", "Alexander", "Anton", "Armin", "Beat", "Benjamin", "Bernd", "Bernhard",
        "Christian", "Christoph", "Daniel", "David", "Dieter", "Dominik", "Elias", "Emil",
        "Erik", "Ernst", "Fabian", "Felix", "Florian", "Frank", "Franz", "Fritz", "Gabriel",
        "Georg", "Gerhard", "Günter", "Guenter", "Hans", "Heinrich", "Heinz", "Helmut",
        "Herbert", "Hermann", "Jakob", "Jan", "Jens", "Joachim", "Johannes", "Jonas", "Josef",
        "Julian", "Jürgen", "Juergen", "Karl", "Klaus", "Konrad", "Kurt", "Leon", "Linus",
        "Lukas", "Manfred", "Manuel", "Marc", "Marcel", "Marco", "Markus", "Martin", "Matthias",
        "Max", "Maximilian", "Michael", "Nico", "Niklas", "Nils", "Noah", "Norbert", "Oliver",
        "Otto", "Patrick", "Paul", "Peter", "Philipp", "Rainer", "Ralf", "Reto", "Richard",
        "Robert", "Roger", "Roland", "Rolf", "Rudolf", "Samuel", "Sebastian", "Simon", "Stefan",
        "Stephan", "Sven", "Thomas", "Thorsten", "Tim", "Tobias", "Ulrich", "Urs", "Uwe",
        "Valentin", "Viktor", "Walter", "Werner", "Wilhelm", "Wolfgang",

        // German speaking, female
        "Andrea", "Angelika", "Anja", "Anna", "Annette", "Antonia", "Barbara", "Beatrice",
        "Bettina", "Birgit", "Brigitte", "Carmen", "Caroline", "Christa", "Christiane",
        "Christina", "Claudia", "Cornelia", "Daniela", "Diana", "Doris", "Elena", "Elisabeth",
        "Elke", "Emma", "Erika", "Eva", "Franziska", "Gabriele", "Gerda", "Gertrud", "Hanna",
        "Hannah", "Heidi", "Helena", "Helga", "Ingrid", "Irene", "Iris", "Jasmin", "Jennifer",
        "Jessica", "Johanna", "Judith", "Julia", "Karin", "Katharina", "Kathrin", "Katja",
        "Klara", "Laura", "Lea", "Lena", "Lena­", "Lisa", "Luisa", "Maja", "Manuela", "Maria",
        "Marianne", "Marie", "Martina", "Melanie", "Michaela", "Mia", "Monika", "Nadine",
        "Natalie", "Nicole", "Nina", "Petra", "Regula", "Renate", "Ruth", "Sabine", "Sandra",
        "Sara", "Sarah", "Silvia", "Simone", "Sofia", "Sophie", "Stefanie", "Susanne", "Tanja",
        "Ursula", "Ute", "Vanessa", "Vera", "Verena", "Veronika", "Yvonne",

        // English speaking, common enough to appear in mixed text
        "Adam", "Alice", "Amy", "Andrew", "Ann", "Anthony", "Barbara", "Ben", "Betty", "Bill",
        "Bob", "Brian", "Carol", "Charles", "Charlotte", "Chris", "Daniel", "Dave", "Deborah",
        "Donald", "Donna", "Dorothy", "Edward", "Elizabeth", "Emily", "George", "Grace",
        "Harry", "Helen", "Henry", "Jack", "James", "Jane", "Janet", "Jason", "Jeff",
        "Jennifer", "Jerry", "Jim", "Joe", "John", "Joseph", "Joshua", "Karen", "Kate",
        "Kevin", "Kimberly", "Larry", "Laura", "Linda", "Lisa", "Margaret", "Mark", "Mary",
        "Matthew", "Michelle", "Nancy", "Nathan", "Nicholas", "Olivia", "Patricia", "Paul",
        "Rachel", "Rebecca", "Richard", "Robert", "Ronald", "Ryan", "Sandra", "Scott", "Sharon",
        "Stephen", "Steven", "Susan", "Timothy", "William",
    };

    public static bool IsKnown(string word) => Names.Contains(word);
}
