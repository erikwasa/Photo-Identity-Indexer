namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0212 v40: additive nullable technical stream facts. Existing metadata,
/// source revisions, capture/Place/person actions and frame evidence are unchanged.
/// No migration reads source media or fills absent measurements with guesses.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoMetadataExtensionSchemaMigration = """
        ALTER TABLE video_revision_metadata
            ADD COLUMN video_bitrate_bps bigint NULL
                CHECK (video_bitrate_bps BETWEEN 1 AND 10000000000),
            ADD COLUMN audio_sample_rate_hz integer NULL
                CHECK (audio_sample_rate_hz BETWEEN 1 AND 384000),
            ADD COLUMN audio_channels integer NULL
                CHECK (audio_channels BETWEEN 1 AND 32),
            ADD COLUMN audio_channel_layout text NULL
                CHECK (char_length(audio_channel_layout) BETWEEN 1 AND 64
                    AND audio_channel_layout !~ '[[:cntrl:]]');
        """;
}
