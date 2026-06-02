namespace MindAttic.Legion;

/// <summary>
/// Gendered first-name pools for the persona library: 512 distinct female and
/// 512 distinct male names, drawn from the most popular US names (SSA, recent
/// years) in popularity order, with near-duplicate spellings collapsed
/// (Soundex) and the two pools kept disjoint. Each of the 1024 personas gets a
/// unique name matching its pronoun set — no last initial.
/// </summary>
internal static class PersonaNames
{
    /// <summary>512 distinct female first names (for she/her personas), most-popular first.</summary>
    internal static readonly string[] Female =
    {
        "Olivia","Emma","Charlotte","Amelia","Sophia","Mia","Isabella","Ava","Evelyn","Luna","Harper","Camila","Eleanor","Elizabeth","Violet","Scarlett",
        "Emily","Hazel","Lily","Gianna","Aurora","Penelope","Aria","Nora","Chloe","Ellie","Mila","Avery","Abigail","Isla","Eliana","Nova",
        "Madison","Zoe","Ivy","Grace","Lucy","Willow","Riley","Naomi","Victoria","Stella","Hannah","Valentina","Delilah","Leah","Lillian","Paisley",
        "Genesis","Madelyn","Sadie","Addison","Natalie","Josephine","Alice","Ruby","Claire","Kinsley","Everly","Emery","Adeline","Kennedy","Maeve","Audrey",
        "Autumn","Eden","Iris","Anna","Eloise","Jade","Maria","Caroline","Brooklyn","Quinn","Aaliyah","Vivian","Gabriella","Hailey","Savannah","Cora",
        "Ariana","Lydia","Allison","Melody","Serenity","Bella","Skylar","Josie","Daisy","Raelynn","Eva","Juniper","Samantha","Hadley","Parker","Julia",
        "Amara","Rose","Charlie","Ashley","Remi","Melanie","Margaret","Piper","Brielle","Freya","Cecilia","Esther","Sienna","Summer","Peyton","Sage",
        "Valerie","Magnolia","Emersyn","Catalina","Margot","Alina","Sloane","Brianna","Oakley","Blakely","Kehlani","Oaklynn","Ximena","Juliette","Mackenzie","Genevieve",
        "Anastasia","Reagan","Katherine","Ember","June","Andrea","Wrenley","Ada","Kaylee","Rosalie","Ariella","Kaia","Ruth","Sara","Jasmine","Phoebe",
        "River","Wren","Presley","Alora","Zuri","Sutton","Noelle","Journee","Evangeline","Aspen","Haven","Blake","Kimberly","Vera","Tatum","Arabella",
        "Diana","Kiara","Harmony","Lilith","Delaney","Collins","Harlow","Blair","Daphne","Faith","Lennon","Stevie","Mariana","Morgan","Juliana","Daniela",
        "Dahlia","Brynlee","Angela","Kamila","Ryleigh","Taylor","Dakota","Talia","Jordyn","Ophelia","Gia","Celeste","Londyn","Palmer","Mabel","Octavia",
        "Finley","Marley","Adelaide","Lucille","Shiloh","Antonella","Maisie","Cataleya","Noa","Brooke","Celine","Hope","Vanessa","Rory","Teagan","Adriana",
        "Rosemary","Kendall","Rebecca","Thea","Amina","Tessa","Esme","Mckenna","Luciana","Catherine","Dream","Annabelle","Esmeralda","Lauren","Fatima","Giselle",
        "Jocelyn","Phoenix","Trinity","Heidi","Meadow","Raya","Paige","Leighton","Raven","Itzel","Laura","Hayden","Winter","Alivia","Francesca","Serena",
        "Gracelynn","Aisha","Gwendolyn","Sabrina","Helen","Astrid","Fiona","Michelle","Xiomara","Melissa","Veronica","Remington","Sylvie","Annalise","Mallory","Elora",
        "Carmen","Matilda","Miracle","Destiny","Colette","Skye","Daleyza","Alexis","Katalina","Felicity","Joy","Armani","Bianca","Dorothy","Stephanie","Fernanda",
        "Lorelai","Renata","Imani","Jimena","Kate","Cameron","Amanda","Nadia","Calliope","Paris","Cassidy","Faye","Bonnie","Edith","Oakleigh","Meredith",
        "Carter","Kamryn","April","Murphy","Ivory","Florence","Alondra","Bristol","Monroe","Lyric","Legacy","Margo","Clementine","Briar","Yaretzi","Jessica",
        "Arleth","Virginia","Avianna","Royalty","Azariah","Kenzie","Holland","Capri","Amber","Miranda","Indie","Mina","Beatrice","Jovie","Ivanna","Nalani",
        "Mavis","Iyla","Charleigh","Chaya","Tiana","Estella","Winnie","Yara","Hadassah","Freyja","Romina","Lennox","Kayleigh","Cassandra","Galilea","Jenesis",
        "Braelynn","Elliott","Gloria","Kataleya","Martha","Irene","Clover","Penny","Karsyn","Flora","Goldie","Fallon","Vienna","Janelle","Aya","Birdie",
        "Liv","Christina","Zelda","Paula","Chelsea","Karla","Chana","Promise","Bethany","Yareli","Adalee","Andi","Kiana","Monica","Dior","Whitley",
        "Zaniyah","Inaya","Angie","Kendra","Marilyn","Emerald","Persephone","Bridget","Ezra","Lenora","Loretta","Novalee","Karina","Georgina","Theodora","Paulina",
        "Lakelynn","Denver","Henley","Zayla","Araceli","Pearl","Hunter","Kamari","Treasure","Tallulah","Veda","Ocean","Iliana","Bellamy","Ashlyn","Zendaya",
        "Linda","Teresa","Artemis","Brittany","Yasmin","Rosalina","Alitzel","Stormi","Cynthia","Zainab","Barbara","Ensley","Waverly","Winona","Emryn","Giuliana",
        "Karter","Liberty","Tiffany","Chandler","Judith","Magdalena","Yamileth","Bria","Amaris","August","Marleigh","Simone","Giovanna","Greta","Etta","Julissa",
        "Nancy","Emmeline","Xyla","Cadence","Blessing","Saoirse","Kassidy","Indigo","Saanvi","Tru","Winifred","Deborah","Sapphire","Seraphina","Quincy","Soleil",
        "Whitney","Natasha","Esperanza","Itzayana","Justice","Kaydence","Bexley","Guinevere","Tinsley","Casey","Avalynn","Egypt","Hadleigh","Ellison","Paisleigh","Kaisley",
        "Austyn","Mazikeen","Clarissa","Landry","Frida","Sandra","Ryder","Ingrid","Denisse","Dalary","Guadalupe","Corinne","Susan","Emani","Harleigh","Erika",
        "Heavenly","Patricia","Tenley","Lindsey","Harriet","Zhavia","Wendy","Janessa","Jayden","Kassandra","Brenda","Lizbeth","Courtney","Desiree","Kristina","Aranza",
        "Spencer","Bryleigh","Montserrat","Monserrat","Tabitha","Cherish","Heather","Kensington","Kayden","Cordelia","Ireland","Aubrianna","Isis","Temperance","Taryn","Diamond",
    };

    /// <summary>512 distinct male first names (for he/him personas), most-popular first.</summary>
    internal static readonly string[] Male =
    {
        "Liam","Noah","Oliver","James","Elijah","Mateo","Theodore","Henry","Lucas","William","Benjamin","Levi","Sebastian","Jack","Michael","Daniel",
        "Leo","Owen","Samuel","Hudson","Alexander","Asher","Luca","Ethan","John","David","Jackson","Joseph","Mason","Julian","Dylan","Maverick",
        "Gabriel","Logan","Aiden","Thomas","Isaac","Miles","Grayson","Santiago","Anthony","Wyatt","Ezekiel","Caleb","Cooper","Charles","Christopher","Isaiah",
        "Nolan","Nathan","Kai","Angel","Lincoln","Andrew","Roman","Adrian","Aaron","Wesley","Ian","Thiago","Axel","Brooks","Bennett","Weston",
        "Rowan","Theo","Beau","Eli","Silas","Jonathan","Leonardo","Walker","Micah","Everett","Robert","Enzo","Jeremiah","Colton","Easton","Landon",
        "Amir","Gael","Austin","Jameson","Xavier","Dominic","Damian","Nicholas","Carson","Atlas","Adriel","Emmett","Harrison","Vincent","Milo","Jasper",
        "Giovanni","Zion","Connor","Sawyer","Arthur","Archer","Lorenzo","Declan","Emiliano","Diego","George","Evan","Graham","Kingston","Nathaniel","Legend",
        "Dawson","Bryson","Calvin","Ivan","Chase","Cole","Ace","Arlo","Dean","Brayden","Jude","Matias","Rhett","Alan","Braxton","Kaiden",
        "Zachary","Jesus","Emmanuel","Adonis","Tyler","Elliot","Emilio","Camden","Stetson","Ryker","Justin","Kevin","Finn","Bentley","Zayden","Felix",
        "Beckett","Tate","Caden","Beckham","Alex","Brody","Tucker","Knox","Hayes","Peter","Timothy","Joel","Edward","Griffin","Xander","Oscar",
        "Victor","Abraham","Brandon","Abel","Richard","Callum","Patrick","Eric","Grant","Israel","Milan","Rafael","Kairo","Elian","Javier","Nico",
        "Ismael","Cohen","Simon","Marcus","Steven","Mark","Dallas","Tristan","Paul","Paxton","Crew","Kash","Kenneth","Omar","Colt","Walter",
        "Emerson","Derek","Muhammad","Kaleb","Preston","Jorge","Kayson","Cade","Tobias","Otto","Atticus","Holden","Martin","Maximiliano","Malcolm","Francisco",
        "Bodhi","Cyrus","Hendrix","Warren","Bryan","Leonel","Onyx","Ali","Jaziel","Saint","Dante","Gideon","Maximus","Colter","Kyler","Zyaire",
        "Harvey","Manuel","Karson","Khalil","Jared","Fernando","Ari","Colson","Kylian","Archie","Banks","Bowen","Kade","Daxton","Jaden","Rhys",
        "Sonny","Zander","Iker","Sullivan","Bradley","Raymond","Odin","Prince","Cesar","Dariel","Orion","Titus","Rylan","Pablo","Chance","Travis",
        "Kohen","Jay","Hector","Marshall","Russell","Baylor","Kameron","Tyson","Grady","Baker","Winston","Julius","Desmond","Royal","Sterling","Mario",
        "Kylo","Sergio","Kashton","Shepherd","Ibrahim","Kobe","Santino","Raiden","Nasir","Forrest","Tanner","Nehemiah","Edgar","Clark","Gunner","Esteban",
        "Hank","Solomon","Wells","Gianni","Noel","Corbin","Tripp","Atreus","Devin","Troy","Fabian","Donovan","Kieran","Leonidas","Kendrick","Ruben",
        "Camilo","Augustus","Memphis","Yusuf","Finnegan","Rodrigo","Uriel","Philip","Andy","Porter","Ridge","Frederick","Ayaan","Dalton","Major","Valentino",
        "Kolton","Ford","Leland","Seth","Jamir","Leandro","Miller","Gregory","Hezekiah","Cassian","Alonzo","Moises","Conrad","Drew","Anakin","Soren",
        "Pierce","Trevor","Ozzy","Roy","Ledger","Saul","Armando","Samson","Braylen","Cassius","Emir","Samir","Gerardo","Albert","Sincere","Arjun",
        "Kamden","Nikolai","Dorian","Layton","Ronald","Davis","Huxley","Reign","Vicente","Salem","Fletcher","Alden","Cannon","Gustavo","Boston","Zeke",
        "Dennis","Madden","Marvin","Otis","Harlan","Azriel","Donald","Amos","Roland","Aarav","Caspian","Finnley","Wilson","Trace","Creed","Jakari",
        "Westley","Hassan","Houston","Tommy","Truett","Abdiel","Ezrah","Zamir","Dexter","Salvador","Uriah","Avyaan","Zaid","Dutton","Skyler","Gage",
        "Wayne","Jiraiya","Carmelo","Loyal","Douglas","Avi","Bridger","Boden","Jefferson","Alvin","Kaiser","Blaze","Quentin","Dakari","Lachlan","Orlando",
        "Yael","Evander","Flynn","Harry","Sevyn","Idris","Ambrose","Yehuda","Nelson","Wes","Bjorn","Watson","Gatlin","Izael","Stanley","Damir",
        "Bear","Kannon","Lance","Melvin","Edison","Eliel","Everest","Yahir","Guillermo","Mitchell","Kingsley","Vihaan","Eddie","Judson","Trenton","Grey",
        "Felipe","Ernesto","Ishaan","Fisher","Leroy","Jedidiah","Ignacio","Ira","Zev","Mustafa","Yahya","Nixon","Demetrius","Langston","Jovanni","Semaj",
        "Curtis","Zavier","Eugene","Alistair","Castiel","Harold","Benedict","Duncan","Yadiel","Imran","Eren","Kolson","Marlon","Adler","Aldo","Osiris",
        "Kartier","Wesson","Mordechai","Randy","Talon","Vance","Boaz","Carl","Kelvin","Foster","Yisroel","Titan","Henrik","Jeremias","Veer","Jadiel",
        "Atharv","Eliezer","Gordon","Stone","Ephraim","Osman","Ulises","Thatcher","Abner","Hollis","Heath","Alaric","Harley","Dangelo","Korbin","Bronson",
    };
}
