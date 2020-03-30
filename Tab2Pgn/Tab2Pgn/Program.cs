using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChessLib.Data;
using ChessLib.Parse.PGN;

namespace TabToPgn
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            string[] lines = System.IO.File.ReadAllLines(args[0]);

            List<(string title, string eco, int ply, string moves)> formatedMoves = BuildMoveLines(lines);
            string preParsedPgn = BuildPreParsedPgn(formatedMoves);
            IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames = await ParseAndValidatePgn(preParsedPgn).ConfigureAwait(false);
            DisplayPgn(parsedGames);
        }

        private static List<(string title, string eco, int ply, string moves)> BuildMoveLines(string[] lines)
        {
            string currentTitle = "";
            string currentECO = "";
            List<string> currentMoves = null;
            List<(string title, string eco, int ply, string moves)> formatedMoves = new List<(string title, string eco, int ply, string moves)>();

            foreach (string line in lines)
            {
                switch (line.Substring(0, 5).ToUpperInvariant())
                {
                    case "TITLE":
                        currentECO = (line.Split("\t"))[1];
                        currentTitle = (line.Split("\t"))[2];
                        break;
                    case "MOVES":
                        currentMoves = (line.Split("\t")).Select(x => x)
                                                         .Where(x => !string.IsNullOrEmpty(x))
                                                         .Skip(1).ToList();

                        formatedMoves.Add((currentTitle, currentECO, currentMoves.Count, TabToPgnFormat(currentMoves)));
                        break;
                    case "COMME":
                        formatedMoves[formatedMoves.Count - 1] = (currentTitle, currentECO, currentMoves.Count, TabToPgnFormat(currentMoves, (line.Split("\t")).Skip(1).ToList()));
                        break;
                    default:
                        break;
                }
            }

            return formatedMoves;
        }

        private static string BuildPreParsedPgn(List<(string title, string eco, int ply, string moves)> formatedMoves)
        {
            DateTime todaysDate = DateTime.UtcNow;
            StringBuilder joinedPgn = new StringBuilder();

            foreach ((string title, string eco, int ply, string moves) in formatedMoves)
            {
                string head = $"[Event \"{title.Replace("\"", "", StringComparison.InvariantCultureIgnoreCase)}\"]\r\n" +
                              $"[Site \"Tab2Pgn\"]\r\n" +
                              $"[UTCDate \"{todaysDate.ToString("yyyy.mm.dd", CultureInfo.CurrentCulture)}\"]\r\n" +
                              $"[UTCTime \"{todaysDate.ToString("hh:mm:ss", CultureInfo.CurrentCulture)}\"]\r\n" +
                              $"[Variant \"Standard\"]\r\n" +
                              $"[Round \"?\"]\r\n" +
                              $"[White \"?\"]\r\n" +
                              $"[Black \"?\"]\r\n" +
                              $"[Result \"*\"]\r\n" +
                              $"[Annotator \"?\"]\r\n" +
                              $"[ECO \"{eco.Replace("\"", "", StringComparison.InvariantCultureIgnoreCase)}\"]\r\n" +
                              $"[Opening \"{title.Replace("\"", "", StringComparison.InvariantCultureIgnoreCase)}\"]\r\n" +
                              $"[PlyCount \"{ply}\"]\r\n" +
                              $"\r\n" +
                              $"{moves}";

                joinedPgn.Append(head);
            }

            return joinedPgn.ToString();
        }

        private static void DisplayPgn(IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames)
        {
            foreach (Game<ChessLib.Data.MoveRepresentation.MoveStorage> game in parsedGames)
            {
                Console.WriteLine(game.ToString());
            }
        }

        private static async Task<IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>>> ParseAndValidatePgn(string preParsedPgn)
        {
            PGNParser v = new ChessLib.Parse.PGN.PGNParser();
            System.Collections.Generic.IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedPgn = await v.GetGamesFromPGNAsync(preParsedPgn.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            return parsedPgn;
        }

        private static string TabToPgnFormat(List<string> currentMoves, List<string> comments = null)
        {
            StringBuilder lineOut = new StringBuilder();
            for (int loop = 0, moveCount = 1; loop < currentMoves.Count; loop++, moveCount = ((loop % 2) == 0) ? moveCount : moveCount + 1)
            {
                string includedComment = (comments == null) ? " " : ((string.IsNullOrEmpty(comments[loop])) ? " " : " {" +
                                         $"{comments[loop]}" + "} " +
                                         (((loop % 2) == 0) ? $"{moveCount}..." : ""));

                if ((loop % 2) == 0)
                {
                    lineOut.Append($"{moveCount}.{currentMoves[loop]}{includedComment}");
                }
                else
                {
                    lineOut.Append($"{currentMoves[loop]}{includedComment}");
                }
            }

            lineOut.Append("*\r\n\r\n");
            return lineOut.ToString();
        }
    }
}
