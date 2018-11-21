using System;
using System.Collections.Generic;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using System.IO;

namespace DdiToCogs
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "DdiToCogs";
            app.HelpOption("-?|-h|--help");

            app.Command("convert", (command) =>
            {

                command.Description = "Convert a DDI 3.2+ schema file to a COGS data model";
                command.HelpOption("-?|-h|--help");

                var locationArgument = command.Argument("[schemaLocation]", "Filename and path to the instance.xsd schema file.");
                var targetArgument = command.Argument("[targetLocation]", "Directory where the COGS datamodel is output.");

                var overwriteOption = command.Option("-o|--overwrite",
                                           "If the target directory exists, delete and overwrite the location",
                                           CommandOptionType.NoValue);

                var typedOption = command.Option("-t|--typed <typedPropertiesFile>",
                                           "Read a file of specified typed relationships for properties",
                                           CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    var location = locationArgument.Value != null
                      ? locationArgument.Value
                      : "instance.xsd";

                    var target = targetArgument.Value != null
                      ? targetArgument.Value
                      : Path.Combine(Directory.GetCurrentDirectory(), "out");

                    var typedLocation = typedOption.Value() != null
                      ? typedOption.Value()
                      : null;

                    bool overwrite = overwriteOption.HasValue();

                    ConvertToCogs converter = new ConvertToCogs();
                    converter.SchemaLocation = location;
                    converter.TargetDirectory = target;
                    converter.Overwrite = overwrite;
                    converter.TypeDefinitionFile = typedLocation;
                    converter.Convert();

                    return 0;
                });

            });


            app.OnExecute(() =>
            {
                Console.WriteLine("Hello World!");
                return 0;
            });

            var result = app.Execute(args);
            Environment.Exit(result);
        }
    }
}