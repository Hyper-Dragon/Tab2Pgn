using ChessLib.Data;
using ChessLib.Parse.PGN;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabToPgn
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            string fileIn = args[0];
            string[] lines = System.IO.File.ReadAllLines(fileIn);

            List<(string title, string eco, int ply, string moves)> formatedMoves = BuildMoveLines(lines);
            string preParsedPgn = BuildPreParsedPgn(formatedMoves);
            IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames = await ParseAndValidatePgn(preParsedPgn).ConfigureAwait(false);
            ValidateMoves(fileIn, parsedGames);
            DisplayPgn(parsedGames);
        }

        private static void ValidateMoves(string fileIn, IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames)
        {
            ChessLib.Data.Types.Enums.Color? repForSide = null;
            SortedDictionary<string, (string pgnEvent, string move)> fenList = new SortedDictionary<string, (string pgnEvent, string move)>();

            if (fileIn.Contains("WHITE", StringComparison.InvariantCultureIgnoreCase))
            {
                repForSide = ChessLib.Data.Types.Enums.Color.White;
            }
            else if (fileIn.Contains("BLACK", StringComparison.InvariantCultureIgnoreCase))
            {
                repForSide = ChessLib.Data.Types.Enums.Color.Black;
            }

            foreach (Game<ChessLib.Data.MoveRepresentation.MoveStorage> game in parsedGames)
            {
                while (game.HasNextMove)
                {
                    game.TraverseForward();

                    if (repForSide != null && game.Board.ActivePlayer == repForSide)
                    {
                        if (game.HasNextMove)
                        {
                            string[] fenSplit = game.CurrentFEN.Split(" ");
                            //string gameKey = $"{fenSplit[0]} {fenSplit[1]} {fenSplit[2]} {fenSplit[3]}";
                            string gameKey = $"{fenSplit[0]} {fenSplit[1]} {fenSplit[2]}";

                            //Console.WriteLine(gameKey);

                            if (fenList.ContainsKey(gameKey))
                            {
                                if (fenList[gameKey].move != game.NextMoveNode.Value.ToString())
                                {
                                    Console.WriteLine($"WARN: Multiple Candidate Moves Detected");
                                    Console.WriteLine($"      {fenList[gameKey].pgnEvent}");
                                    Console.WriteLine($"      {game.TagSection["Event"]} ({game.Board.ActivePlayer.ToString()} Move {game.Board.FullmoveCounter.ToString()})");
                                    Console.WriteLine($"      {gameKey.PadRight(75)} -> {fenList[gameKey].move}/{game.NextMoveNode.Value.ToString()}");
                                    Console.WriteLine("");
                                }
                            }
                            else
                            {
                               
                                fenList.Add(gameKey, ($"{game.TagSection["Event"]} ({game.Board.ActivePlayer.ToString()} Move {game.Board.FullmoveCounter.ToString()})", game.NextMoveNode.Value.ToString()));
                            }
                        }
                    }
                }

                game.GoToInitialState();
            }

            Console.WriteLine("");
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
            PGNParser pgnParser = new ChessLib.Parse.PGN.PGNParser();
            System.Collections.Generic.IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedPgn = await pgnParser.GetGamesFromPGNAsync(preParsedPgn.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
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
