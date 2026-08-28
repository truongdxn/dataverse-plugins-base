using System;

namespace Dataverse.Plugins.Abstractions.Registration
{
    /// <summary>
    /// Declares a pre/post entity image on a plugin step, for the cases the
    /// <see cref="PluginStepAttribute.PreImage"/> and <see cref="PluginStepAttribute.PostImage"/>
    /// shorthands cannot express: a custom image name or alias, <see cref="ImageType.Both"/>, or a
    /// message property other than Target.
    /// <para>
    /// Prefer the shorthand where it fits. It puts the image on the step itself, so there is no
    /// <see cref="StepName"/> to keep in sync.
    /// </para>
    /// <para>
    /// When the class declares a single step the image binds to it automatically. When it declares
    /// several, set <see cref="StepName"/> to the target step's
    /// <see cref="PluginStepAttribute.Name"/>; an unbound image is added to every step on the class.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [PluginImage(ImageType.Both, "Snapshot", StepName = "Contact update: sync",
    ///     Attributes = new[] { Contact.Fields.FirstName })]
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PluginImageAttribute : Attribute
    {
        /// <param name="imageType">Pre-image, post-image, or both.</param>
        /// <param name="name">
        /// Image name and, unless <see cref="EntityAlias"/> is set, the alias used to read it from
        /// the execution context.
        /// </param>
        /// <param name="attributes">
        /// Column logical names to include. Pass none for all columns, which is worth avoiding on
        /// wide tables.
        /// </param>
        public PluginImageAttribute(ImageType imageType, string name, params string[] attributes)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An image must have a name.", nameof(name));
            }

            ImageType = imageType;
            Name = name;
            Attributes = attributes ?? new string[0];
        }

        /// <summary>Pre-image, post-image, or both.</summary>
        public ImageType ImageType { get; }

        /// <summary>Image name.</summary>
        public string Name { get; }

        /// <summary>Column logical names, or empty for all columns.</summary>
        public string[] Attributes { get; }

        /// <summary>Alias used to read the image from context. Defaults to <see cref="Name"/>.</summary>
        public string EntityAlias { get; set; }

        /// <summary>
        /// Binds this image to one specific step when the class declares several. Must match that
        /// step's <see cref="PluginStepAttribute.Name"/> exactly; a value matching no step fails
        /// the build rather than silently dropping the image.
        /// </summary>
        public string StepName { get; set; }

        /// <summary>Message property the image is taken from. Defaults to "Target".</summary>
        public string MessagePropertyName { get; set; }
    }
}
