module FunctionsInFSharp.sendUsaClimateForecast

open System
open System.IO
open System.Text.RegularExpressions
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Logging
open FSharp.Data

/// Function retrieves monthly climate outlook from NOAA to send to Telegram
[<FunctionName("sendUsaClimateForecastFunction")>]
let run ([<TimerTrigger("0 0 17 21 * *")>] myTimer: TimerInfo, log: ILogger) =
    log.LogInformation <| sprintf "Running NOAA weather update map at %A" DateTime.Now
    
    let apiTelegramKey = Environment.GetEnvironmentVariable("APIKEY", EnvironmentVariableTarget.Process)
    
    let submitToTelegram weatherImage =
        async {
            let! res =
                Http.AsyncRequest
                    ("https://api.telegram.org/bot" + apiTelegramKey + "/sendPhoto?chat_id="
                     + System.Web.HttpUtility.UrlEncode("-414892076"),
                     body =
                         Multipart
                             (boundary = "b322328f-346b-43e5-ab84-a7f9926fb5a6",
                              parts = [ MultipartItem("photo", "climate_map.gif", weatherImage) ]))
            do log.LogInformation(sprintf "Telegram gave back a status code of: %i" res.StatusCode) }
        |> Async.RunSynchronously
    
    //Send map to Telegram from NOAA 30-day outlook
    let weatherMapUri = "https://www.cpc.ncep.noaa.gov/products/predictions/30day/off15_temp.gif"
    let res = Http.Request(weatherMapUri, silentHttpErrors=true)
    
    match res.Body with
    | Text text -> log.LogInformation <| sprintf "Should have gotten a pic here but instead got: %A" text
    | Binary bytes -> submitToTelegram (new MemoryStream(bytes))
    
    //Send text of outlook
    let (|Forecast|_|) forecast  =
        let m = Regex.Match(forecast, "30-DAY.+----",RegexOptions.Singleline)
        if m.Success then Some m.Value else None
    
    let html = HtmlDocument.Load("https://www.cpc.ncep.noaa.gov/products/predictions/long_range/fxus07.html")
    html.Descendants ["pre"]
    |> Seq.head
    |> (fun x -> match x.InnerText() with
                 | Forecast text -> Http.Request ("https://api.telegram.org/bot" + apiTelegramKey + "/sendMessage?chat_id="
                     + System.Web.HttpUtility.UrlEncode("-414892076"),
                                                  body = FormValues [
                                                      "text", text.Substring(0, Math.Min(4096, text.Length))
                                                  ]
                                              ) |> ignore
                 | _ -> () ) //if there's no text, we'll just do nothing