using Inspectrol.Core.Devices;

namespace Inspectrol.Tests;

public class RecordingTagReaderTests
{
    [Fact]
    public void Reads_model_and_firmware()
    {
        var tag = RecordingTagReader.Read(Mp4Builder.WithTag("EMTOAtlaS_1.0.3"));

        Assert.NotNull(tag);
        Assert.Equal("AtlaS", tag.ModelTag);
        Assert.Equal(new Version(1, 0, 3), tag.Firmware);
    }

    [Fact]
    public void Keeps_the_original_case_of_the_model()
    {
        // The site writes the model both as "Atlas" and as "AtlaS", so the tag keeps its original case.
        var tag = RecordingTagReader.Read(Mp4Builder.WithTag("EMTOSPARTA_1.0.1"));

        Assert.Equal("SPARTA", tag!.ModelTag);
    }

    [Fact]
    public void Reads_model_names_with_underscores()
    {
        var tag = RecordingTagReader.Read(Mp4Builder.WithTag("EMTOSCAT_SE_2.11.0"));

        Assert.Equal("SCAT_SE", tag!.ModelTag);
        Assert.Equal(new Version(2, 11, 0), tag.Firmware);
    }

    [Fact]
    public void Returns_nothing_when_there_is_no_tag()
    {
        Assert.Null(RecordingTagReader.Read(Mp4Builder.WithoutTag()));
    }

    [Fact]
    public void Returns_nothing_when_there_is_no_moov()
    {
        Assert.Null(RecordingTagReader.Read(Mp4Builder.WithoutMoov()));
    }

    [Fact]
    public void Survives_a_truncated_file()
    {
        // A recording can be cut off when the car loses power.
        Assert.Null(RecordingTagReader.Read(Mp4Builder.Truncated()));
    }

    [Fact]
    public void Survives_a_file_that_is_not_mp4_at_all()
    {
        var path = Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}.MP4");
        File.WriteAllText(path, "это вообще не видео");

        Assert.Null(RecordingTagReader.Read(new FileInfo(path)));
    }

    [Fact]
    public void Survives_a_missing_file()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}.MP4"));

        Assert.Null(RecordingTagReader.Read(missing));
    }

    [Fact]
    public void Reads_the_model_from_an_avi_recording()
    {
        var tag = RecordingTagReader.Read(AviBuilder.WithModel("BARRACUDA"));

        Assert.NotNull(tag);
        Assert.Equal("BARRACUDA", tag.ModelTag);
        Assert.Null(tag.Firmware);
    }

    [Fact]
    public void Does_not_drag_the_gps_track_into_the_model_name()
    {
        var tag = RecordingTagReader.Read(AviBuilder.WithGpsRightAfterModel("BARRACUDA"));

        Assert.Equal("BARRACUDA", tag!.ModelTag);
    }

    [Fact]
    public void Finds_the_model_inside_nested_lists()
    {
        var tag = RecordingTagReader.Read(AviBuilder.WithModelInsideLists("BARRACUDA"));

        Assert.Equal("BARRACUDA", tag!.ModelTag);
    }

    [Fact]
    public void Does_not_take_a_chance_match_from_video_data()
    {
        var tag = RecordingTagReader.Read(AviBuilder.WithFakeStrdInVideoData("BARRACUDA"));

        Assert.Equal("BARRACUDA", tag!.ModelTag);
    }

    [Fact]
    public void Survives_a_truncated_avi()
    {
        Assert.Null(RecordingTagReader.Read(AviBuilder.Truncated()));
    }

    [Fact]
    public void Returns_nothing_for_an_avi_without_a_model()
    {
        Assert.Null(RecordingTagReader.Read(AviBuilder.WithoutModel()));
    }
}
