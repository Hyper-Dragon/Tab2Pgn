﻿using ChessLib.Data;
using ChessLib.Parse.PGN;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
            BuildMoveImage(parsedGames,true);
            DisplayPgn(parsedGames);
        }


        private async static void BuildMoveImage(IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames, bool isFromWhitesPerspective = true)
        {
            string BOARD_DOWNLOAD_SIZE = "0";
            string IS_BOARD_FLIPPED = isFromWhitesPerspective ? "": "&flip=true";

            SortedList<string, string> lastMoveNameList = new();

            // Create a new pen.
            using Pen orangePen = new Pen(Brushes.Orange)
            {
                Width = 4.0F
            };

            string BOARD_FEN = @"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
            Image startBoardImage = null;
            int moveCount = 0;
            int maxWidth = 0;

            using (var webClient = new WebClient())
            {
                using var startBoardImgStream = new MemoryStream(webClient.DownloadData(@"https://www.chess.com/dynboard?board=green&fen=" + BOARD_FEN + @"&piece=space&size="+BOARD_DOWNLOAD_SIZE+IS_BOARD_FLIPPED));
                startBoardImage = Image.FromStream(startBoardImgStream);
            }

            List<SortedList<string,(string, string, Image, string)>> moveLines = new();
            moveLines.Add(new SortedList<string,(string,string,Image, string)>());
            moveLines[0].Add(BOARD_FEN, ("",BOARD_FEN, startBoardImage,"") );

            foreach (Game<ChessLib.Data.MoveRepresentation.MoveStorage> game in parsedGames)
            {
                Image bmp = null;
                string moveKey = BOARD_FEN;
                moveCount = 0;

                while (game.HasNextMove)
                {
                    game.TraverseForward();

                    string[] fenSplit = game.CurrentFEN.Split(" ");
                    string gameKey = $"{fenSplit[0]}";
                    moveKey += gameKey;

                    if (moveLines.Count <= ++moveCount) { moveLines.Add(new SortedList<string, (string, string, Image, string)>()); }

                    if (!moveLines[moveCount].ContainsKey($"{moveKey}"))
                    {
                        using (HttpClient httpClient = new HttpClient())
                        {
                            var response = httpClient.GetStreamAsync(@"https://www.chess.com/dynboard?board=green&fen=" + gameKey + "&piece=space&size=" + BOARD_DOWNLOAD_SIZE +IS_BOARD_FLIPPED).Result;
                            bmp = Bitmap.FromStream(response);
                        }
                        
                        moveLines[moveCount].Add($"{moveKey}", (game.CurrentMoveNode.Value.SAN, $"{gameKey}", bmp, game.CurrentMoveNode.Value.Comment));
                    }

                    if (moveCount+1 < moveLines.Count)
                    {
                        int addedCount = moveLines[moveCount + 1].Where(x => x.Key.StartsWith(moveKey)).Count();
                        if (addedCount >= 1)
                        {
                            moveLines[moveCount].Add($"{moveKey}{addedCount}", ("", "", null,""));
                        }
                    }

                    maxWidth = Math.Max(maxWidth, moveLines[moveCount].Count);

                    if (!game.HasNextMove)
                    {
                        lastMoveNameList.Add(moveKey, game.TagSection["Opening"]);
                    }
                }

                game.GoToInitialState();
            }


            for(int loopY=1 ; loopY < (moveLines.Count-1) ; loopY++)
            {
                for(int loopX=0; loopX < moveLines[loopY].Count; loopX++)
                {
                    if (moveLines[loopY].Values[loopX].Item3 != null)
                    {
                        int addedCount = moveLines[loopY + 1].Where(x => x.Key.StartsWith(moveLines[loopY].Keys[loopX])).Count();

                        if (addedCount == 0)
                        {
                            for (int loopInnerY=loopY+1; loopInnerY < moveLines.Count; loopInnerY++)
                            {
                                moveLines[loopInnerY].Add($"{moveLines[loopY].Keys[loopX]}{addedCount}", ("", "", null,""));
                            }
                        }
                    }
                }

                maxWidth = Math.Max(maxWidth, moveLines[loopY].Count);
            }


            string BKG_URL = @"https://images.chesscomfiles.com/uploads/v1/theme/101328-0.caa989e5.jpeg";
            TextureBrush bkgBrush = null;
            int SPACER_SIZE = 60;
            int BOARD_SIZE = startBoardImage.Width; //160;
            int BOX_WIDTH = 80;
            int BOX_HEIGHT = 30;
            int BLOCK_SIZE = BOARD_SIZE + SPACER_SIZE;

            // Create font and brush.
            using Font drawFont = new Font(FontFamily.GenericSansSerif, 14f);
            Brush drawBrush = Brushes.White;

            using (var webClient = new WebClient())
            {
                using var bkgImgStream = new MemoryStream(webClient.DownloadData(BKG_URL));
                Image bkgImage = Image.FromStream(bkgImgStream);
                bkgBrush = new TextureBrush(bkgImage);
            }

            using (var image = new Bitmap(maxWidth*BLOCK_SIZE, moveLines.Count*BLOCK_SIZE))
            {
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    
                    graphics.FillRectangle(bkgBrush,0,0,image.Width,image.Height);

                    for (int loopY = 0; loopY < moveLines.Count; loopY++)
                    {
                        var moveLine = moveLines[loopY].OrderBy(x => x.Key).ToArray();

                        for (int loopX = 0; loopX < moveLine.Length; loopX++)
                        {
                            //Draw Connector
                            if (loopY + 1 < moveLines.Count)
                            {
                                for (int loopNextRowX = 0; loopNextRowX < moveLines[loopY + 1].Count; loopNextRowX++)
                                {
                                    if (moveLines[loopY + 1].Keys[loopNextRowX].Contains(moveLines[loopY].Keys[loopX]))
                                    {
                                        if (moveLines[loopY + 1].Values[loopNextRowX].Item3 != null)
                                        {
                                            graphics.DrawLine(orangePen,
                                                              (loopX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2),
                                                              (loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE),
                                                              (loopX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2),
                                                              (loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE) + (SPACER_SIZE / 2));

                                            graphics.DrawLine(orangePen,
                                                              (loopNextRowX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2),
                                                              (loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE) + (SPACER_SIZE / 2),
                                                              (loopNextRowX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2),
                                                              ((loopY + 1) * BLOCK_SIZE) + (SPACER_SIZE / 2));

                                            graphics.DrawLine(orangePen,
                                                              (loopX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2),
                                                              (loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE) + (SPACER_SIZE / 2),
                                                              (loopNextRowX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2),
                                                              ((loopY + 1) * BLOCK_SIZE));
                                        }
                                    }
                                }
                            }
                        }
                    }


                    for (int loopY = 0; loopY < moveLines.Count; loopY++)
                    {
                        var moveLine = moveLines[loopY].OrderBy(x => x.Key).ToArray();

                        for (int loopX = 0; loopX < moveLine.Length; loopX++)
                        {



                            if (lastMoveNameList.ContainsKey(moveLines[loopY].Keys[loopX]))
                            {
                                using var drawFormat = new System.Drawing.StringFormat() { FormatFlags = StringFormatFlags.DirectionVertical };

                                graphics.DrawString(lastMoveNameList[moveLines[loopY].Keys[loopX]],
                                                    drawFont,
                                                    drawBrush,
                                                    ((loopX * BLOCK_SIZE) + (SPACER_SIZE / 2)) - (BOX_WIDTH / 3),
                                                    10,//((loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE)) - (BOX_HEIGHT / 2),
                                                    drawFormat);
                            }


                            if (loopY+1 < moveLines.Count)
                            {
                                for (int loopNextRowX = 0; loopNextRowX < moveLines[loopY + 1].Count; loopNextRowX++)
                                {
                                    if (moveLines[loopY + 1].Keys[loopNextRowX].Contains(moveLines[loopY].Keys[loopX]))
                                    {
                                        if (moveLines[loopY + 1].Values[loopNextRowX].Item3 != null)
                                        {
                                            graphics.FillRectangle(Brushes.Black,
                                                                   ((loopNextRowX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2)) - (BOX_WIDTH / 2),
                                                                   ((loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE) + (SPACER_SIZE / 2)) - (BOX_HEIGHT / 2),
                                                                   BOX_WIDTH,
                                                                   BOX_HEIGHT);

                                            graphics.DrawString(moveLines[loopY + 1].Values[loopNextRowX].Item1, 
                                                                drawFont,
                                                                drawBrush,
                                                                ((loopNextRowX * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE / 2)) - (BOX_WIDTH / 2) + 4,
                                                                ((loopY * BLOCK_SIZE) + (SPACER_SIZE / 2) + (BOARD_SIZE) + (SPACER_SIZE / 2)) - (BOX_HEIGHT / 2) + 4);
                                        }
                                    }
                                }
                            }

                            //Draw Board
                            if (moveLine[loopX].Value.Item3 != null)
                            {
                                graphics.DrawImage(moveLine[loopX].Value.Item3, (loopX * BLOCK_SIZE) + (SPACER_SIZE / 2), (loopY * BLOCK_SIZE) + (SPACER_SIZE / 2));
                            }
                        }
                    }

                    image.Save(@"C:\Dropbox\ChessStats\resized.png", ImageFormat.Png);
                }
            }
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
