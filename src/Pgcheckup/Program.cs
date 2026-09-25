using System.CommandLine;

var root = new RootCommand("Checks a PostgreSQL database for the problems that cause outages.");
return root.Parse(args).Invoke();
