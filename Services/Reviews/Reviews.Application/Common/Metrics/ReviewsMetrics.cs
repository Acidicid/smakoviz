using System.Diagnostics.Metrics;

namespace Reviews.Application.Common.Metrics;

/// <summary>
/// Бізнес-метрики «Відгуків» (Лаб. 07): Meter "Reviews.Api" (підключено в ServiceDefaults), Singleton.
/// Теги з низькою кардинальністю: review.target — restaurant | dish | courier; review.rating — 1..5.
/// </summary>
public sealed class ReviewsMetrics
{
    public const string MeterName = "Reviews.Api";

    private readonly Counter<long> _created;
    private readonly Histogram<int> _rating;

    public ReviewsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("reviews.created", unit: "{review}", description: "Created reviews by target and rating");
        _rating = meter.CreateHistogram<int>("reviews.rating", unit: "{star}", description: "Distribution of review ratings");
    }

    public void ReviewCreated(string target, int rating)
    {
        _created.Add(1,
            new KeyValuePair<string, object?>("review.target", target),
            new KeyValuePair<string, object?>("review.rating", rating));
        _rating.Record(rating, new KeyValuePair<string, object?>("review.target", target));
    }
}
